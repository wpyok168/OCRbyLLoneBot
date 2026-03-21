using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TouchSocket.Core;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace OCRbyLLoneBot
{
    public partial class Form1 : Form
    {
        private ConcurrentDictionary<string, RecMsgMode> ocrResmsgmode = new ConcurrentDictionary<string, RecMsgMode>();
        WebSocketClient webSocket = new WebSocketClient();
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            CreateSocket();
            //await SendActivationRequest("424531638745335609955640716633530643857085064476161468457574401");
        }

        private async void CreateSocket()
        {
            // 重新实例化WebSocketClient（确保和TcpClient无关联）
            webSocket = new WebSocketClient();
            await webSocket.SetupAsync(new TouchSocketConfig()
                  .ConfigureContainer(a =>
                  {
                      a.AddConsoleLogger();
                  })
                  .ConfigurePlugins(a =>
                  {
                      a.AddWebSocketConnectedPlugin((client, e) =>
                      {
                          client.Logger.Info("WebSocket连接已建立");
                          client.PingAsync().ContinueWith(pingTask =>
                          {
                              if (pingTask.IsCompletedSuccessfully)
                              {
                                  client.Logger.Info("WebSocket连接已成功建立");
                              }
                              else
                              {
                                  client.Logger.Error("WebSocket连接建立失败");
                              }
                          });

                          return EasyTask.CompletedTask;
                      });

                  })
                  .SetRemoteIPHost("ws://127.0.0.1:7780"));


            webSocket.Closed = (c, e) =>
            {
                Console.WriteLine("Closed");
                MessageBox.Show("ws://127.0.0.1:7780 服务器连接关闭");
                return EasyTask.CompletedTask;
            };
            webSocket.Connected = (c, e) =>
            {
                webSocket.Logger.Info("通过ws://127.0.0.1:7780 连接成功");
                return EasyTask.CompletedTask;
            };
            webSocket.Received = async (c, e) =>
            {
                switch (e.DataFrame.Opcode)
                {
                    case WSDataType.Cont:
                        break;
                    case WSDataType.Text:
                        Console.WriteLine(e.DataFrame.ToText());
                        string recmsg = e.DataFrame.ToText();
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
                //return EasyTask.CompletedTask;
            };
            await webSocket.ConnectAsync();
        }

        private async Task MsgAction(JsonDocument jsonDocument)
        {
            RecMsgMode msg = GetRecMsgMode(jsonDocument);

            ////解析OCR消息，提取文本内容
            //(StringBuilder, StringBuilder) ocrResult;
            //if (!string.IsNullOrEmpty(msg.Echo))
            //{
            //    ocrResult = GetOCRTextConet(jsonDocument);
            //    string echo = msg.Echo;
            //    msg = ocrResmsgmode.TryGetValue(echo, out var ocrrecMsgMode) ? ocrrecMsgMode : msg;
            //    ocrResmsgmode.TryRemove(echo, out _);
            //    await SendMsg(msg, ocrResult.Item1.ToString());
            //}
            //return;

            string iid = string.Empty;
            string ocrstr = string.Empty;
            bool falg = false;
            //解析OCR消息，提取文本内容
            (StringBuilder, StringBuilder) ocrResult;
            if (!string.IsNullOrEmpty(msg.Echo))
            {
                ocrResult = GetOCRTextConet(jsonDocument);
                string echo = msg.Echo;
                msg = ocrResmsgmode.TryGetValue(echo, out var ocrrecMsgMode) ? ocrrecMsgMode : msg;
                ocrResmsgmode.TryRemove(echo, out _);
                if (!string.IsNullOrEmpty(ocrResult.Item2.ToString()) && (ocrResult.Item2.ToString().Length == 54 || ocrResult.Item2.ToString().Length == 63))
                {
                    iid = CleanIID(ocrResult.Item2.ToString());
                }
                else
                {
                    ocrstr = ocrResult.Item1.ToString();
                    falg = true;
                }
            }
            else
            {
                iid = CleanIID(msg.RecMsgContent);
            }

            if (falg)
            {
                await SendMsg(msg, ocrstr);
                return;
            }

            if (!string.IsNullOrEmpty(iid))
            {
                //var response = await client.GetCID(iid, nd);
                var response = await SendActivationRequest(iid);
                //dynamic? responseObj = JsonSerializer.Deserialize<dynamic>(response);
                JsonDocument jdjson = JsonDocument.Parse(response);
                var root = jdjson.RootElement;
                // 封装通用方法：安全获取JSON字段值
                string GetJsonProperty(JsonElement element, string propName)
                {
                    if (element.TryGetProperty(propName, out JsonElement propEle))
                    {
                        // 根据字段类型返回对应值（兼容字符串/数字）
                        return propEle.ValueKind switch
                        {
                            JsonValueKind.String => propEle.GetString() ?? "空字符串",
                            JsonValueKind.Number => propEle.ToString(), // 数字转字符串
                            JsonValueKind.Null => "字段为空",
                            _ => $"不支持的类型：{propEle.ValueKind}"
                        };
                    }
                    return "字段不存在";
                }

                var sb = new System.Text.StringBuilder();

                // 安全获取各字段（修正拼写错误：iic → iid）
                sb.AppendLine("IID：" + GetJsonProperty(root, "iid"));
                sb.AppendLine("CID：" + GetJsonProperty(root, "cid"));
                sb.AppendLine("productName：" + GetJsonProperty(root, "productName"));
                sb.AppendLine("PID：" + GetJsonProperty(root, "pid"));
                sb.AppendLine("maxInstallCount：" + GetJsonProperty(root, "maxInstallCount"));
                //sb.AppendLine("Type：" + GetJsonProperty(root, "PidLicenseChannel"));

                // 处理 message 字段（不显示指定内容）
                string message = GetJsonProperty(root, "message");
                if (!string.Equals(
                        message,
                        "Clearinghouse Supplied Confirmation ID",
                        StringComparison.OrdinalIgnoreCase) &&
                    message != "字段不存在" &&
                    message != "字段为空")
                {
                    sb.AppendLine("message：" + message);
                }

                string sendmsg = sb.ToString().TrimEnd();
                await SendMsg(msg, sendmsg);

            }
            if (msg.RecMsgContent != null)
            {
                if (msg.RecMsgContent.Contains("加黑") || msg.RecMsgContent.Contains("移黑") || msg.RecMsgContent.Contains("解黑"))
                {
                    await SendMsg(msg, "");
                }
            }

            return;
        }
        private RecMsgMode GetRecMsgMode(JsonDocument recmsgdic)
        {
            RecMsgMode recmsgMode = new RecMsgMode() { RecMsgContent = "" };
            //System.Text.Json.JsonDocument recmsgdic = System.Text.Json.JsonDocument.Parse(recmsg);
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
                        //if (item.TryGetProperty("data", out JsonElement data) && data.TryGetProperty("text", out JsonElement text))
                        //{
                        //    recmsgMode.RecMsgContent = text.GetString();
                        //}
                        // 提取文本内容
                        if (item.TryGetProperty("type", out JsonElement type) && type.GetString() == "text")
                        {
                            if (item.TryGetProperty("data", out JsonElement data) && data.TryGetProperty("text", out JsonElement text))
                            {
                                recmsgMode.RecMsgContent = text.GetString() ?? "";
                            }
                        }
                        // 提取图片的file和url
                        if (item.TryGetProperty("type", out JsonElement imgType) && imgType.GetString() == "image")
                        {
                            if (item.TryGetProperty("data", out JsonElement imgData))
                            {
                                // 提取file字段
                                if (imgData.TryGetProperty("file", out JsonElement file))
                                {
                                    recmsgMode.ImageFile = file.GetString();
                                }

                                // 提取url字段
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
        private (StringBuilder, StringBuilder) GetOCRTextConet(JsonDocument recmsgDoc)
        {
            StringBuilder listtext = new StringBuilder();
            StringBuilder iidgroup = new StringBuilder();
            int matchCount = 0; // 限制最多匹配2次

            // 空值防护
            if (recmsgDoc == null)
            {
                return (listtext, iidgroup);
            }

            // 先收集所有文本（不影响原逻辑）
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

            // 第一次匹配：只匹配6位数字
            if (matchCount < 2)
            {
                matchCount++;
                var sixDigitMatches = Regex.Matches(listtext.ToString(), @"\b\d{6}\b");
                iidgroup.Clear(); // 清空后重新拼接
                foreach (Match match in sixDigitMatches)
                {
                    iidgroup.Append(match.Value);
                }
            }

            // 检查第一次结果长度，若≠54则第二次匹配7位数字
            if (iidgroup.Length != 54 && matchCount < 2)
            {
                matchCount++;
                var sevenDigitMatches = Regex.Matches(listtext.ToString(), @"\b\d{7}\b");
                iidgroup.Clear(); // 清空后重新拼接
                foreach (Match match in sevenDigitMatches)
                {
                    iidgroup.Append(match.Value);
                }
            }

            return (listtext, iidgroup);
        }

        /// <summary>
        /// 清理 IID，去掉空格、- 和非数字字符，返回纯数字字符串
        /// </summary>
        /// <param name="iid">原始 IID</param>
        /// <returns>纯数字 IID</returns>
        public static string CleanIID(string iid)
        {
            if (string.IsNullOrEmpty(iid))
                return string.Empty;

            // 去除所有非数字字符
            string cleaned = Regex.Replace(iid, @"\D", "");

            // 限定长度 54 或 63
            if (cleaned.Length == 54 || cleaned.Length == 63)
                return cleaned;

            return string.Empty; // 长度不合法
        }

        private async Task SendMsg(RecMsgMode rec, string sendmsg, string type = "server")
        {

            try
            {

                bool isSelfMessage = rec.UserID == rec.Self_ID;

                if (isSelfMessage)
                {
                    // 忽略自己发的消息，防止死循环
                    return;
                }

                //JsonDocument recmsgdic = System.Text.Json.JsonDocument.Parse(recmsg);
                if (!string.IsNullOrEmpty(rec.RecMsgContent) || !string.IsNullOrEmpty(rec.ImageFile))
                {

                    if (rec.IsFriend) //好友消息
                    {
                        //sendmsg = sendmsg.Replace("\r", "\\r").Replace("\n", "\\n");
                        //string msg1 = $@"{{""action"":""send_private_msg"",""params"":{{""user_id"":{rec.UserID},""message"":""{sendmsg.ToString()}"",""auto_escape"":false}}, ""echo"":""""}}";
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
                    else //if (subtype.GetString().Equals("group") && !string.IsNullOrEmpty(msgtext))
                    {
                        //message_type":"group"，"sub_type":"normal" 群聊  "message_type":"private"，"sub_type":"group" 群私聊  "message_type":"private"，"sub_type":"friend" 好友
                        if (rec.Message_Type.Equals("private"))//群私聊消息
                        {
                            //sendmsg = sendmsg.Replace("\r", "\\r").Replace("\n", "\\n");
                            //string msg1 = $"{{\"action\":\"send_msg\",\"params\":{{\"message_type\":\"private\", \"user_id\":{rec.UserID},\"group_id\":{rec.GroupID}, \"message\":\"{sendmsg}\",\"auto_escape\":false}}, \"echo\":\"\"}}";
                            // 构建匿名对象
                            var msgPayload = new
                            {
                                action = "send_msg",
                                @params = new // params 是 C# 关键字，需要加 @ 前缀
                                {
                                    message_type = "private",
                                    user_id = rec.UserID,
                                    group_id = rec.GroupID,
                                    message = sendmsg, // JsonSerializer 会自动处理这里的转义
                                    auto_escape = false
                                },
                                echo = ""
                            };
                            string msg1 = JsonSerializer.Serialize(msgPayload,new JsonSerializerOptions {
                                WriteIndented = false,
                                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                            });
                            await SendServerMsg(rec.Self_ID.ToString(), msg1);
                        }
                        else if (rec.Message_Type.Equals("group"))//群消息
                        {
                            //sendmsg = sendmsg.Replace("\r", "\\r").Replace("\n", "\\n") + $"\\r\\n[CQ:at,qq={rec.UserID}]";
                            sendmsg = sendmsg + $"\r\n[CQ:at,qq={rec.UserID}]";
                            //string msg1 = $"{{\"action\":\"send_group_msg\",\"params\":{{\"group_id\":{rec.GroupID}, \"message\":\"{sendmsg}\",\"auto_escape\":false}}, \"echo\":\"\"}}";
                            var msgPayload = new
                            {
                                action = "send_group_msg",
                                @params = new  // @避开params关键字
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


        private async Task SendServerMsg(string selfId, string msg)
        {
            await webSocket.SendAsync(msg);
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
                requestBuilder.AppendLine("Authorization: Bearer govUrlID");
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

        /// <summary>
        /// 从 HTTP 完整响应中拆分出 Body 部分（JSON）
        /// </summary>
        private string ExtractRealJson(string fullResponse)
        {
            // 1. 先找到 HTTP 头结束位置
            int headerEnd = fullResponse.IndexOf("\r\n\r\n");
            if (headerEnd == -1) return null;
            string body = fullResponse.Substring(headerEnd + 4);

            // 2. 找到第一个 { （JSON 开始）
            int jsonStart = body.IndexOf('{');
            // 3. 找到最后一个 } （JSON 结束）
            int jsonEnd = body.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                return body.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }

            return null;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                // 释放WebSocket资源，避免端口占用
                if (webSocket != null && webSocket.Online)
                {
                    webSocket.CloseAsync().Wait(5000); // 5秒超时
                    webSocket.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("释放WebSocket资源异常：" + ex.Message);
            }
        }
    }
}
