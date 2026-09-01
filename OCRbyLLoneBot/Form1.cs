using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;
using Sunny.UI.Win32;
using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace OCRbyLLoneBot
{
    public partial class Form1 : Sunny.UI.UIForm
    {
        private ConcurrentDictionary<string, RecMsgMode> ocrResmsgmode = new ConcurrentDictionary<string, RecMsgMode>();

        // ==================== 按参考示例替换服务变量 ====================
        private HttpService service = new HttpService();
        /// <summary>存储所有LLOneBot WebSocket客户端，和示例BotWebSocketMap对应</summary>
        //private readonly ConcurrentDictionary<IWebSocketClient, bool> BotWebSocketMap = new ConcurrentDictionary<IWebSocketClient, bool>();
        private readonly ConcurrentDictionary<IWebSocket, bool> BotWebSocketMap = new ConcurrentDictionary<IWebSocket, bool>();

        // 黑名单JSON文件路径
        private readonly string _blackConfigPath = Path.Combine(Application.StartupPath, "blacklist_config.json");
        // 内存缓存黑白名单配置
        private BotBlackConfig? _blackConfig;

        public Form1()
        {
            InitializeComponent();
            // 程序启动加载黑名单配置
            LoadBlackConfig();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.toolStripLabel1.Text = "机器人Http+Ws服务端，ws://127.0.0.1:7781/ws";
            this.Style = Sunny.UI.UIStyle.Purple;
            CreateSocket();
        }

        // ==================== 完全参照你给的CreateServer示例重写 ====================
        private async void CreateSocket()
        {
            TouchSocketConfig config = new TouchSocketConfig();

            config.SetListenIPHosts(7781)
                 .ConfigureContainer(a =>
                 {
                     a.AddConsoleLogger();
                 })
                 .ConfigurePlugins(a =>
                 {
                     // 启用WebSocket中间件
                     a.UseWebSocket(options =>
                     {
                         options.SetUrl("/ws");
                         options.SetAutoPong(true);

                         // 连接校验逻辑，仅允许/ws路径升级
                         options.SetVerifyConnection((client, context) =>
                         {
                             if (!context.Request.IsUpgrade())
                                 return false;

                             if (context.Request.UrlEquals("/ws"))
                                 return true;

                             return false;
                         });
                     });

                     // WebSocket建立连接时存入Map
                     a.AddWebSocketConnectedPlugin((wsClient, e) =>
                     {
                         BotWebSocketMap.TryAdd(wsClient, true);
                         service.Logger.Info($"LLOneBot客户端接入，Id={wsClient.Client.IP}");
                         return EasyTask.CompletedTask;
                     });

                     // ====== 新增：WebSocket报文接收插件（替换service.Received赋值）======
                     a.AddWebSocketReceivedPlugin(async (wsClient, e) =>
                     {
                         switch (e.DataFrame.Opcode)
                         {
                             case WSDataType.Text:
                                 string recmsg = e.DataFrame.ToText();
                                 Console.WriteLine(recmsg);
                                 await MsgAction(JsonDocument.Parse(recmsg));
                                 break;
                             case WSDataType.Binary:
                                 byte[] by = e.DataFrame.PayloadData.ToArray();
                                 break;
                             case WSDataType.Close:
                                 break;
                             case WSDataType.Ping:
                                 break;
                             case WSDataType.Pong:
                                 break;
                             default:
                                 break;
                         }
                     });


                     // WebSocket断开时移除Map（和示例一致）
                     a.AddWebSocketClosedPlugin((ws, e) =>
                     {
                         BotWebSocketMap.Remove(ws, out _);
                         service.Logger.Info($"客户端断开，Id={ws.Client.IP}");
                         return EasyTask.CompletedTask;
                     });
                 });

            try
            {
                await service.SetupAsync(config);
                await service.StartAsync();
                service.Logger.Info("WebSocket服务器已启动，地址: ws://127.0.0.1:7781/ws");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Http+Ws服务启动失败: {ex.Message}，使用本地模式");
                this.toolStripLabel1.Text = "本地模式";
            }
            
        }

        // ==================== 群发消息适配BotWebSocketMap ====================
        private async Task SendServerMsg(string selfId, string msg)
        {
            foreach (var wsClient in BotWebSocketMap.Keys)
            {
                if (wsClient.Online)
                {
                    await wsClient.SendAsync(msg);
                }
            }
        }

        // ==================== 下方所有业务逻辑【完全原样不动，无修改】 ====================
        /// <summary>
        /// 接收WS推送消息统一处理入口
        /// 区分OCR图片回调消息 / 普通文字消息，自动提取、校验IID并查询激活信息
        /// </summary>
        /// <param name="jsonDocument">LLOneBot推送的原始消息JSON</param>
        private async Task MsgAction(JsonDocument jsonDocument)
        {
            // 解析基础消息结构体
            RecMsgMode msg = GetRecMsgMode(jsonDocument);

            // 核心：机器人账号发送的消息直接拦截，不处理不回复（解决互刷）
            if (IsInBlackList(msg.UserID))
            {
                return;
            }

            // 黑白名单指令拦截（加黑/移黑/解黑），发送空消息阻断流程
            //if (msg.RecMsgContent != null)
            //{
            //    if (msg.RecMsgContent.Contains("加黑") || msg.RecMsgContent.Contains("移黑") || msg.RecMsgContent.Contains("解黑"))
            //    {
            //        await SendMsg(msg, "");
            //    }
            //}

            #region 黑白名单指令处理（管理员权限控制）
            if (!string.IsNullOrEmpty(msg.RecMsgContent))
            {
                string text = msg.RecMsgContent.Trim();
                // 正则匹配指令：加黑/解黑 + QQ数字
                Match addBlockMatch = Regex.Match(text, @"^加黑(\d+)");
                Match removeBlockMatch = Regex.Match(text, @"^(移黑|解黑)(\d+)");

                if (addBlockMatch.Success || removeBlockMatch.Success)
                {
                    // 非管理员直接返回无权限提示
                    if (!IsAdmin(msg.UserID))
                    {
                        await SendMsg(msg, "❌ 权限不足，仅管理员可执行加黑/解黑操作");
                        return;
                    }

                    long targetQq;
                    string replyMsg = string.Empty;
                    if (addBlockMatch.Success)
                    {
                        targetQq = long.Parse(addBlockMatch.Groups[1].Value);
                        if (_blackConfig?.AllBlackQqList?.Contains(targetQq) == true)
                        {
                            replyMsg = $"✅ QQ {targetQq} 已在黑名单内，无需重复添加";
                        }
                        else
                        {
                            _blackConfig?.AllBlackQqList.Add(targetQq);
                            SaveBlackConfig(); // 写入JSON保存
                            replyMsg = $"✅ 成功将QQ {targetQq} 加入黑名单";
                        }
                    }
                    else
                    {
                        targetQq = long.Parse(removeBlockMatch.Groups[2].Value);
                        // 禁止管理员把机器人自身从黑名单移除（防止机器人互发刷屏）
                        // 可自行删除此判断，如果允许移出机器人账号
                        if (targetQq == _blackConfig?.AdminQQ)
                        {
                            replyMsg = "⚠️ 不能将管理员账号移出黑名单";
                        }
                        else if (_blackConfig?.AllBlackQqList?.Remove(targetQq) == true)
                        {
                            SaveBlackConfig();
                            replyMsg = $"✅ 成功将QQ {targetQq} 移出黑名单";
                        }
                        else
                        {
                            replyMsg = $"⚠️ QQ {targetQq} 不在黑名单内";
                        }
                    }
                    await SendMsg(msg, replyMsg);
                    return;
                }
            }
            #endregion


            string iid = string.Empty;          // 最终校验通过的标准IID
            string ocrstr = string.Empty;       // OCR识别失败时返回给用户的原图文本
            bool falg = false;                  // 标记OCR是否未识别到合法IID
            (StringBuilder, StringBuilder) ocrResult; // OCR返回：完整识别文本、提取的数字串

            if (string.IsNullOrEmpty(msg.Echo) && !string.IsNullOrEmpty(msg.ImageFile))
            {
                return;
            }

            #region OCR图片回调分支（带echo标识，图片识别结果返回）
            if (!string.IsNullOrEmpty(msg.Echo))
            {
                // 获取OCR识别结果
                ocrResult = GetOCRTextConet(jsonDocument);
                string echo = msg.Echo;
                // 根据echo取回发起OCR时缓存的原始消息对象
                msg = ocrResmsgmode.TryGetValue(echo, out var ocrrecMsgMode) ? ocrrecMsgMode : msg;
                // 清理缓存，防止内存堆积
                ocrResmsgmode.TryRemove(echo, out _);

                // 获取OCR全部原始识别文本，送入工具一站式清洗+提取IID
                string fullOcrText = ocrResult.Item1.ToString();
                List<string> validIidList = IidValidator.GetValidIidFromOcr(fullOcrText);

                if (validIidList.Any())
                {
                    // 取第一个校验完全合法的IID
                    iid = validIidList.First();
                }
                else
                {
                    // OCR未提取到合法IID，生成标准化错误提示
                    var fullCheck = IidValidator.ValidateIID(fullOcrText);
                    var errInfo = IidValidator.GetErrorText(fullCheck);
                    string errReply = $"❌ 图片识别的IID校验失败\n【OCR完整文本】\n提示：{errInfo.MainText}\n详情：{errInfo.DetailText}\nOCR文本：{fullOcrText}";
                    // 发送错误给用户
                    await SendMsg(msg, errReply);
                    falg = true;
                }
            }
            
            #endregion

            #region 普通文字消息分支（无echo，用户直接发文字）
            else
            {
                string msgText = msg.RecMsgContent;
                // 提取带横杠/空格分段格式的候选IID（7位/6位9段标准排版）
                var splitCandidates = IidValidator.ExtractSplitIidWithSeparator(msgText);
                string targetIid = string.Empty;
                // 用于汇总多条IID校验失败的错误信息
                StringBuilder errorSb = new StringBuilder();

                // 遍历所有分段格式候选IID，逐个校验
                if (splitCandidates.Any())
                {
                    foreach (var candidate in splitCandidates)
                    {
                        var res = IidValidator.ValidateIID(candidate);
                        if (res.Valid)
                        {
                            // 找到第一条合法IID直接使用，终止循环
                            targetIid = res.CleanedIid;
                            break;
                        }
                        // 当前候选IID校验失败，拼接格式化错误信息
                        var errInfo = IidValidator.GetErrorText(res);
                        errorSb.AppendLine($"【IID】{candidate}");
                        errorSb.AppendLine($"提示：{errInfo.MainText}");
                        errorSb.AppendLine($"详情：{errInfo.DetailText}");
                        errorSb.AppendLine("------------------------");
                    }
                }
                var fullRes = IidValidator.ValidateIID(msgText);
                // 兜底逻辑：未匹配到标准分段IID时，全文整体清洗校验
                if (string.IsNullOrEmpty(targetIid))
                {
                    if (fullRes.Valid)
                    {
                        targetIid = fullRes.CleanedIid;
                    }
                    else
                    {

                        // 空安全判断Error
                        string? errCode = fullRes.Error;
                        if (errCode != "not_numeric")
                        {
                            var errInfo = IidValidator.GetErrorText(fullRes);
                            errorSb.AppendLine("【全文内容整体校验】");
                            errorSb.AppendLine($"提示：{errInfo.MainText}");
                            errorSb.AppendLine($"详情：{errInfo.DetailText}");

                            // 长度错误直接回复并终止
                            if (errCode == "invalid_length")
                            {
                                await SendMsg(msg, errorSb.ToString().TrimEnd());
                                return;
                            }
                        }
                    }
                }

                iid = targetIid;

                // 无任何合法IID，统一汇总错误回复并终止流程，不请求激活接口
                if (string.IsNullOrEmpty(iid) && errorSb.Length > 0 && fullRes.FailedBlocks.Count > 0)
                {
                    string reply = "❌ IID校验不通过，请检查格式后重新发送\n\n" + errorSb.ToString().TrimEnd();
                    await SendMsg(msg, reply);
                    return;
                }
            }
            #endregion

            // OCR识别无合法IID，直接返回识别文本，不执行IID查询
            if (falg)
            {
                return;
            }

            // 存在合法IID，调用微软IID激活验证接口查询信息
            if (!string.IsNullOrEmpty(iid))
            {
                var response = await SendActivationRequest(iid);
                JsonDocument jdjson = JsonDocument.Parse(response);
                var root = jdjson.RootElement;

                /// <summary>
                /// 安全读取JSON指定字段，兼容字符串/数字/空值
                /// </summary>
                string GetJsonProperty(JsonElement element, string propName)
                {
                    if (element.TryGetProperty(propName, out JsonElement propEle))
                    {
                        return propEle.ValueKind switch
                        {
                            JsonValueKind.String => propEle.GetString() ?? "空字符串",
                            JsonValueKind.Number => propEle.ToString(),
                            JsonValueKind.Null => "字段为空",
                            _ => $"不支持的类型：{propEle.ValueKind}"
                        };
                    }
                    return "字段不存在";
                }

                // 组装返回给用户的IID详情文本
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("IID：" + GetJsonProperty(root, "iid"));
                sb.AppendLine("CID：" + GetJsonProperty(root, "cid"));//FormatActivationId(raw1, separator: "-")
                sb.AppendLine("CID：" + FormatActivationId(GetJsonProperty(root, "cid"), separator: "-"));
                sb.AppendLine("productName：" + GetJsonProperty(root, "productName"));
                sb.AppendLine("PID：" + GetJsonProperty(root, "pid"));
                sb.AppendLine("maxInstallCount：" + GetJsonProperty(root, "maxInstallCount"));

                // 过滤默认无用提示文案，仅展示异常message
                string message = GetJsonProperty(root, "message");
                //if (!string.Equals(
                //        message,
                //        "Clearinghouse Supplied Confirmation ID",
                //        StringComparison.OrdinalIgnoreCase) &&
                //    message != "字段不存在" &&
                //    message != "字段为空")
                //{
                //    sb.AppendLine("message：" + message);
                //}
                sb.AppendLine("message：" + message);

                string sendmsg = sb.ToString().TrimEnd();
                await SendMsg(msg, sendmsg);
            }

            return;
        }

        /// <summary>
        /// 格式化 Windows 激活 ID。
        /// 自动剥离输入中已有的分隔符(空格/连字符/制表符)，只保留数字后按 groupSize 分组。
        /// 校验规则：CID = 48 位(8组×6)，IID = 54 位(9组×6)，其他位数抛异常。
        /// </summary>
        public static string FormatActivationId(string raw, int groupSize = 6, string separator = " ")
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException("输入不能为空", nameof(raw));

            // 1) 清洗：只保留数字
            string digits = new string(raw.Where(char.IsDigit).ToArray());

            // 2) 校验：CID 48 位，IID 54 位
            if (digits.Length is not (48 or 54))
                throw new ArgumentException(
                    $"位数不合法：期望 48(CID) 或 54(IID) 位，实际 {digits.Length} 位", nameof(raw));

            if (groupSize <= 0 || digits.Length % groupSize != 0)
                throw new ArgumentException("分组大小不合法", nameof(groupSize));

            // 3) 分组拼接
            return string.Join(separator,
                Enumerable.Range(0, digits.Length / groupSize)
                          .Select(i => digits.Substring(i * groupSize, groupSize)));
        }


        private RecMsgMode GetRecMsgMode(JsonDocument recmsgdic)
        {
            RecMsgMode recmsgMode = new RecMsgMode() { RecMsgContent = "" };
            if (recmsgdic.RootElement.TryGetProperty("self_id", out JsonElement self_id))
            {
                recmsgMode.Self_ID = self_id.GetInt64();
            }
            if (recmsgdic.RootElement.TryGetProperty("group_id", out JsonElement group_id))
            {
                recmsgMode.GroupID = group_id.GetInt64();
            }
            if (recmsgdic.RootElement.TryGetProperty("user_id", out JsonElement user_id))
            {
                recmsgMode.UserID = user_id.GetInt64();
            }
            if (recmsgdic.RootElement.TryGetProperty("sub_type", out JsonElement sub_type))
            {
                recmsgMode.IsFriend = sub_type.GetString()?.Equals("friend") ?? false;
                recmsgMode.IsGroupPrivate = sub_type.GetString()?.Equals("group") ?? false;
            }
            if (recmsgdic.RootElement.TryGetProperty("message_type", out JsonElement messagetype))
            {
                recmsgMode.Message_Type = messagetype.GetString() ?? "";
            }
            if (recmsgdic.RootElement.TryGetProperty("time", out JsonElement time))
            {
                recmsgMode.Time = time.GetInt64();
            }
            if (recmsgdic.RootElement.TryGetProperty("echo", out JsonElement echo))
            {
                recmsgMode.Echo = echo.GetString() ?? "";
            }
            if (recmsgdic.RootElement.TryGetProperty("message", out JsonElement message))
            {
                if (message.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in message.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out JsonElement type) && type.GetString() == "text")
                        {
                            if (item.TryGetProperty("data", out JsonElement data) && data.TryGetProperty("text", out JsonElement text))
                            {
                                recmsgMode.RecMsgContent = text.GetString() ?? "";
                            }
                        }
                        if (item.TryGetProperty("type", out JsonElement imgType) && imgType.GetString() == "image")
                        {
                            if (item.TryGetProperty("data", out JsonElement imgData))
                            {
                                if (imgData.TryGetProperty("file", out JsonElement file))
                                {
                                    recmsgMode.ImageFile = file.GetString();
                                }
                                if (imgData.TryGetProperty("url", out JsonElement url))
                                {
                                    recmsgMode.ImageUrl = url.GetString() ?? "";
                                }
                            }
                        }
                    }
                }
            }
            if (!string.IsNullOrEmpty(recmsgMode.ImageFile))
            {
                ocrResmsgmode.TryAdd(recmsgMode.ImageFile, recmsgMode);
                GetOCRText(recmsgMode);
            }
            return recmsgMode;
        }

        private async void GetOCRText(RecMsgMode rec, string type = "server")
        {
            string msg1 = $@"{{""action"":""ocr_image"",""params"":{{""image"":""{rec.ImageFile}"",""auto_escape"":false}}, ""echo"":""{rec.ImageFile}""}}";
            await SendServerMsg(rec.Self_ID.ToString(), msg1);
        }

        //private (StringBuilder, StringBuilder) GetOCRTextConet(JsonDocument recmsgDoc)
        //{
        //    StringBuilder listtext = new StringBuilder();
        //    StringBuilder iidgroup = new StringBuilder();
        //    int matchCount = 0;
        //    if (recmsgDoc == null)
        //    {
        //        return (listtext, iidgroup);
        //    }
        //    if (recmsgDoc.RootElement.TryGetProperty("data", out JsonElement data) &&
        //        data.TryGetProperty("texts", out JsonElement texts) &&
        //        texts.ValueKind == JsonValueKind.Array)
        //    {
        //        foreach (var item in texts.EnumerateArray())
        //        {
        //            if (item.TryGetProperty("text", out JsonElement textElement) &&
        //                textElement.ValueKind == JsonValueKind.String)
        //            {
        //                string text = textElement.GetString() ?? string.Empty;
        //                listtext.AppendLine(text);
        //            }
        //        }
        //    }
        //    if (matchCount < 2)
        //    {
        //        matchCount++;
        //        var sixDigitMatches = Regex.Matches(listtext.ToString(), @"\b\d{6}\b");
        //        iidgroup.Clear();
        //        foreach (Match match in sixDigitMatches)
        //        {
        //            iidgroup.Append(match.Value);
        //        }
        //    }
        //    if (iidgroup.Length != 54 && matchCount < 2)
        //    {
        //        matchCount++;
        //        var sevenDigitMatches = Regex.Matches(listtext.ToString(), @"\b\d{7}\b");
        //        iidgroup.Clear();
        //        foreach (Match match in sevenDigitMatches)
        //        {
        //            iidgroup.Append(match.Value);
        //        }
        //    }
        //    return (listtext, iidgroup);
        //}

        /// <summary>
        /// 解析OCR返回文本，输出完整识别文本 + 提取到的合法IID（复用IidValidator全套OCR清洗逻辑）
        /// </summary>
        private (StringBuilder, StringBuilder) GetOCRTextConet(JsonDocument recmsgDoc)
        {
            StringBuilder listtext = new StringBuilder();
            StringBuilder iidgroup = new StringBuilder();

            if (recmsgDoc == null)
            {
                return (listtext, iidgroup);
            }

            // 1. 拼接全部OCR原始文本到listtext（保留原有逻辑不变）
            if (recmsgDoc.RootElement.TryGetProperty("data", out JsonElement data) &&
                data.TryGetProperty("texts", out JsonElement texts) &&
                texts.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in texts.EnumerateArray())
                {
                    if (item.TryGetProperty("text", out JsonElement textElement) &&
                        textElement.ValueKind == JsonValueKind.String)
                    {
                        string text = textElement.GetString() ?? string.Empty;
                        listtext.AppendLine(text);
                    }
                }
            }

            string fullOcrRaw = listtext.ToString();
            // 2. 使用工具类一站式清洗、提取、校验所有合法IID
            List<string> validIidList = IidValidator.GetValidIidFromOcr(fullOcrRaw);

            // 3. 取第一个合法IID填入iidgroup，无则保持空
            if (validIidList.Any())
            {
                iidgroup.Append(validIidList.First());
            }

            // 不再使用原来matchCount、6位/7位分段拼接逻辑，全部移除
            return (listtext, iidgroup);
        }


        private async Task SendMsg(RecMsgMode rec, string sendmsg, string type = "server")
        {
            try
            {
                bool isSelfMessage = rec.UserID == rec.Self_ID;
                if (isSelfMessage)
                {
                    return;
                }
                if (!string.IsNullOrEmpty(rec.RecMsgContent) || !string.IsNullOrEmpty(rec.ImageFile))
                {
                    if (rec.IsFriend)
                    {
                        var msgPayload = new
                        {
                            action = "send_private_msg",
                            @params = new
                            {
                                user_id = rec.UserID,
                                message = sendmsg,
                                auto_escape = false
                            },
                            echo = ""
                        };
                        string msg1 = JsonSerializer.Serialize(msgPayload, new JsonSerializerOptions
                        {
                            WriteIndented = false,
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        });
                        await SendServerMsg(rec.Self_ID.ToString(), msg1);
                    }
                    else
                    {
                        if (rec.Message_Type.Equals("private"))
                        {
                            var msgPayload = new
                            {
                                action = "send_msg",
                                @params = new
                                {
                                    message_type = "private",
                                    user_id = rec.UserID,
                                    group_id = rec.GroupID,
                                    message = sendmsg,
                                    auto_escape = false
                                },
                                echo = ""
                            };
                            string msg1 = JsonSerializer.Serialize(msgPayload, new JsonSerializerOptions
                            {
                                WriteIndented = false,
                                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                            });
                            await SendServerMsg(rec.Self_ID.ToString(), msg1);
                        }
                        else if (rec.Message_Type.Equals("group"))
                        {
                            sendmsg = sendmsg + $"\r\n[CQ:at,qq={rec.UserID}]";
                            var msgPayload = new
                            {
                                action = "send_group_msg",
                                @params = new
                                {
                                    group_id = rec.GroupID,
                                    message = sendmsg,
                                    auto_escape = false
                                },
                                echo = ""
                            };
                            string msg1 = JsonSerializer.Serialize(msgPayload, new JsonSerializerOptions
                            {
                                WriteIndented = false,
                                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                            });
                            await SendServerMsg(rec.Self_ID.ToString(), msg1);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }

        public async Task<string> SendActivationRequest(string iid)
        {
            if (string.IsNullOrWhiteSpace(iid))
                throw new Exception("无效的 IID");

            string host = "visualsupport.microsoft.com";
            int port = 443;
            string apiPath = "/api/productActivation/validateIID";

            // 声明TcpClient为局部变量，并用using确保自动释放
            using var client = new TouchSocket.Sockets.TcpClient();
            var responseBuilder = new StringBuilder();
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); // 异步续传，避免线程阻塞

            try
            {
                string dpopToken = DpopTokenGenerator.GenerateDpopToken(apiPath);
                int numberOfDigits = iid.Length / 9;

                var data = await GetTokenDataAsync();
                string token = data["id_token"]!.ToString();
                //dynamic data = await GetTokenDataDynamicAsync();
                //string token = data.access_token; // ✅ 和 JS 写法一样

                // 构建JSON（改用序列化，避免拼接错误）
                var requestBody = new
                {
                    IID = iid,
                    ProductType = "windows",
                    productGroup = "Windows",
                    productName = "Windows 11",
                    numberOfDigits = numberOfDigits,
                    Country = "CHN",
                    Region = "APGC",
                    InstalledDevices = 1,
                    OverrideStatusCode = "MUL",
                    InitialReasonCode = "45164"
                };
                string jsonBody = JsonSerializer.Serialize(requestBody);
                byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

                // 构建HTTP请求头
                StringBuilder requestBuilder = new StringBuilder();
                requestBuilder.AppendLine($"POST {apiPath} HTTP/1.1");
                requestBuilder.AppendLine($"Host: {host}");
                requestBuilder.AppendLine("Content-Type: application/json");
                //requestBuilder.AppendLine("Authorization: Bearer govUrlID");
                requestBuilder.AppendLine($"Authorization: Bearer {token}");
                requestBuilder.AppendLine($"DPoP: {dpopToken}");
                requestBuilder.AppendLine("x-session-id: app_mmsj2c31_x1nrlz06b");
                //requestBuilder.AppendLine($"Referer: https://{host}/{govUrlConfig}/activate");
                requestBuilder.AppendLine($"Content-Length: {bodyBytes.Length}");
                requestBuilder.AppendLine("Connection: close");
                requestBuilder.AppendLine();
                byte[] headerBytes = Encoding.UTF8.GetBytes(requestBuilder.ToString());

                // 解析IP（优先IPv4）
                var ipAddresses = await Dns.GetHostAddressesAsync(host);
                var targetIp = ipAddresses.First(ip => ip.AddressFamily == AddressFamily.InterNetwork);

                // 配置TcpClient（关键：独立配置，不共享WebSocket的容器/日志）
                var tcpConfig = new TouchSocketConfig()
                    .SetRemoteIPHost($"{targetIp}:{port}")
                    .SetClientSslOption(options =>
                    {
                        // 忽略证书验证（访问公网HTTPS建议保留，避免证书问题）
                        options.CertificateValidationCallback = (sender, cert, chain, errors) => true;
                        // 关闭证书吊销检查
                        options.CheckCertificateRevocation = false;
                        // 微软接口不需要客户端证书，注释掉（如果是你的私有接口需要则打开）
                        // options.ClientCertificates = new X509Certificate2Collection() { new X509Certificate2("client.pfx", "pwd") };
                        // SSL协议：访问微软接口不能设为None，指定Tls12/Tls13（否则握手失败）
                        options.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
                        // 目标主机必须和域名一致（微软接口是visualsupport.microsoft.com）
                        options.TargetHost = host;
                    })
                    // 核心：禁用全局容器，完全隔离资源
                    .ConfigureContainer(container =>
                    {
                        // 什么都不做，让它生成一个全新的独立容器
                        // 或者手动清空注册（如果TouchSocket版本支持）
                        // container.RemoveRegisteredTypes();
                    })
                    // 禁用日志共享，避免和WebSocket日志冲突
                    .ConfigurePlugins(plugins =>
                    {
                        // 不添加任何插件，保持空
                    });

                // 注册回调（仅针对当前TcpClient）
                client.Received = (c, e) =>
                {
                    var mes = e.Memory.Span.ToString(Encoding.UTF8);
                    responseBuilder.Append(mes);
                    return EasyTask.CompletedTask;
                };

                client.Closed = (c, e) =>
                {
                    // 仅完成当前TcpClient的任务，不影响全局
                    tcs.TrySetResult(responseBuilder.ToString());
                    return EasyTask.CompletedTask;
                };

                // 连接并发送数据
                await client.SetupAsync(tcpConfig);
                await client.ConnectAsync();
                await client.SendAsync(headerBytes);
                await client.SendAsync(bodyBytes);

                // 30秒超时保护
                if (await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30))) == tcs.Task)
                {
                    string fullResponse = await tcs.Task;
                    string jsonResult = ExtractRealJson(fullResponse);
                    return jsonResult ?? "未获取到有效响应";
                }
                else
                {
                    throw new TimeoutException("微软激活验证接口请求超时（30秒）");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"激活请求异常：{ex.Message}");
                // 确保任务完成，避免死等
                tcs.TrySetException(ex);
                throw;
            }
            finally
            {
                // 显式关闭并释放TcpClient，不影响WebSocket
                if (client.Online)
                {
                    await client.CloseAsync();
                }
                client.Dispose(); // 强制释放资源
            }
        }

        private string ExtractRealJson(string fullResponse)
        {
            int headerEnd = fullResponse.IndexOf("\r\n\r\n");
            if (headerEnd == -1) return null;
            string body = fullResponse.Substring(headerEnd + 4);
            int jsonStart = body.IndexOf('{');
            int jsonEnd = body.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                return body.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }
            return null;
        }

        private static readonly System.Net.Http.HttpClient _tokenHttpClient = new System.Net.Http.HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        public static async Task<JsonNode> GetTokenDataAsync()
        {
            // TLS设置仍然放在这个方法内部，满足你的需求
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

            try
            {
                string json = await _tokenHttpClient.GetStringAsync("https://cidtoken.x2ray.cfd");
                JsonNode data = JsonNode.Parse(json)!;

                if (data == null || data["id_token"] == null || string.IsNullOrEmpty(data["id_token"]!.ToString()))
                {
                    throw new Exception("无效的 Token 数据，返回id_token字段为空");
                }
                return data;
            }
            catch (TaskCanceledException)
            {
                throw new Exception("获取token请求超时");
            }
            catch (HttpRequestException ex)
            {
                string innerMsg = ex.InnerException != null ? $" Inner:{ex.InnerException.Message}" : "";
                throw new Exception($"获取Token接口网络请求失败：{ex.Message}{innerMsg}");
            }
            catch (JsonException ex)
            {
                throw new Exception($"Token接口JSON解析失败：{ex.Message}");
            }
            catch (Exception ex)
            {
                string innerMsg = ex.InnerException != null ? $" Inner:{ex.InnerException.Message}" : "";
                throw new Exception($"获取Token未知异常：{ex.Message}{innerMsg}");
            }
        }

        public static async Task<dynamic> GetTokenDataDynamicAsync()
        {
            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            string json = await httpClient.GetStringAsync("https://cidtoken.x2ray.cfd");
            return JsonSerializer.Deserialize<dynamic>(json)!;
        }

        // ==================== 窗口关闭释放HttpService ====================
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (service != null)
                {
                    service.StopAsync().Wait(3000);
                    service.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("释放HttpService资源异常：" + ex.Message);
            }
        }

        private async void textBox1_Click(object sender, EventArgs e)
        {
            string rawText = Clipboard.GetText();
            var valResult = IidValidator.ValidateIID(rawText);

            if (!valResult.Valid)
            {
                // 新增：一键获取格式化中文错误，替代手动拼接字符串
                var errInfo = IidValidator.GetErrorText(valResult);
                this.textBox1.Text = $"{errInfo.MainText}\n{errInfo.DetailText}";
                return;
            }

            string standardIid = valResult.CleanedIid;
            this.textBox1.Text = standardIid;
            this.textBox2.Text = "正在获取。。。，请稍候，若长期无反应，请联系作者。";

            var jsonstr = await SendActivationRequest(standardIid);
            if (jsonstr == null) return;
            JsonDocument jdjson = JsonDocument.Parse(jsonstr);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            string formattedJson = JsonSerializer.Serialize(jdjson.RootElement, options);
            this.textBox2.Text = formattedJson;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.textBox1.Clear();
            this.textBox2.Clear();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Clipboard.SetText(this.textBox2.Text);
        }


        //=============================黑名单配置JSON文件读写=============================
        #region JSON 文件读写工具方法
        /// <summary>加载JSON黑名单，无文件则生成默认模板</summary>
        private void LoadBlackConfig()
        {
            if (!File.Exists(_blackConfigPath))
            {
                // 默认配置：填入管理员QQ、两个机器人QQ到统一黑名单
                _blackConfig = new BotBlackConfig
                {
                    AdminQQ = 414725048, // 你的管理员QQ
                    AllBlackQqList = new List<long> { 11111111, 22222222 } // 两个机器人QQ
                };
                SaveBlackConfig();
                return;
            }

            string jsonText = File.ReadAllText(_blackConfigPath, Encoding.UTF8);
            _blackConfig = JsonSerializer.Deserialize<BotBlackConfig>(jsonText)!;
        }

        /// <summary>内存配置写入JSON文件持久化</summary>
        private void SaveBlackConfig()
        {
            string jsonText = JsonSerializer.Serialize(_blackConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_blackConfigPath, jsonText, Encoding.UTF8);
        }

        /// <summary>判断是否为管理员</summary>
        private bool IsAdmin(long qq) => _blackConfig != null && qq == _blackConfig.AdminQQ;

        /// <summary>判断QQ是否在黑名单（机器人/封禁用户共用）</summary>
        private bool IsInBlackList(long qq) => _blackConfig != null && _blackConfig.AllBlackQqList.Contains(qq);
        #endregion

    }
}