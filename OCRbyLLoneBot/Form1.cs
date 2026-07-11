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

        public Form1()
        {
            InitializeComponent();
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
                service.Logger.Info("WebSocket服务器已启动，地址: ws://127.0.0.1:7780/ws");
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
        private async Task MsgAction(JsonDocument jsonDocument)
        {
            RecMsgMode msg = GetRecMsgMode(jsonDocument);

            string iid = string.Empty;
            string ocrstr = string.Empty;
            bool falg = false;
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
                var response = await SendActivationRequest(iid);
                JsonDocument jdjson = JsonDocument.Parse(response);
                var root = jdjson.RootElement;
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

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("IID：" + GetJsonProperty(root, "iid"));
                sb.AppendLine("CID：" + GetJsonProperty(root, "cid"));
                sb.AppendLine("productName：" + GetJsonProperty(root, "productName"));
                sb.AppendLine("PID：" + GetJsonProperty(root, "pid"));
                sb.AppendLine("maxInstallCount：" + GetJsonProperty(root, "maxInstallCount"));

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

        private (StringBuilder, StringBuilder) GetOCRTextConet(JsonDocument recmsgDoc)
        {
            StringBuilder listtext = new StringBuilder();
            StringBuilder iidgroup = new StringBuilder();
            int matchCount = 0;
            if (recmsgDoc == null)
            {
                return (listtext, iidgroup);
            }
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
            if (matchCount < 2)
            {
                matchCount++;
                var sixDigitMatches = Regex.Matches(listtext.ToString(), @"\b\d{6}\b");
                iidgroup.Clear();
                foreach (Match match in sixDigitMatches)
                {
                    iidgroup.Append(match.Value);
                }
            }
            if (iidgroup.Length != 54 && matchCount < 2)
            {
                matchCount++;
                var sevenDigitMatches = Regex.Matches(listtext.ToString(), @"\b\d{7}\b");
                iidgroup.Clear();
                foreach (Match match in sevenDigitMatches)
                {
                    iidgroup.Append(match.Value);
                }
            }
            return (listtext, iidgroup);
        }

        public static string CleanIID(string iid)
        {
            if (string.IsNullOrEmpty(iid))
                return string.Empty;
            string cleaned = Regex.Replace(iid, @"\D", "");
            if (cleaned.Length == 54 || cleaned.Length == 63)
                return cleaned;
            return string.Empty;
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
            using var client = new TouchSocket.Sockets.TcpClient();
            var responseBuilder = new StringBuilder();
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                string dpopToken = DpopTokenGenerator.GenerateDpopToken(apiPath);
                int numberOfDigits = iid.Length / 9;
                var data = await GetTokenDataAsync();
                string token = data["id_token"]!.ToString();
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
                StringBuilder requestBuilder = new StringBuilder();
                requestBuilder.AppendLine($"POST {apiPath} HTTP/1.1");
                requestBuilder.AppendLine($"Host: {host}");
                requestBuilder.AppendLine("Content-Type: application/json");
                requestBuilder.AppendLine($"Authorization: Bearer {token}");
                requestBuilder.AppendLine($"DPoP: {dpopToken}");
                requestBuilder.AppendLine("x-session-id: app_mmsj2c31_x1nrlz06b");
                requestBuilder.AppendLine($"Content-Length: {bodyBytes.Length}");
                requestBuilder.AppendLine("Connection: close");
                requestBuilder.AppendLine();
                byte[] headerBytes = Encoding.UTF8.GetBytes(requestBuilder.ToString());
                var ipAddresses = await Dns.GetHostAddressesAsync(host);
                var targetIp = ipAddresses.First(ip => ip.AddressFamily == AddressFamily.InterNetwork);
                var tcpConfig = new TouchSocketConfig()
                    .SetRemoteIPHost($"{targetIp}:{port}")
                    .SetClientSslOption(options =>
                    {
                        options.CertificateValidationCallback = (sender, cert, chain, errors) => true;
                        options.CheckCertificateRevocation = false;
                        options.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
                        options.TargetHost = host;
                    })
                    .ConfigureContainer(container => { })
                    .ConfigurePlugins(plugins => { });
                client.Received = (c, e) =>
                {
                    var mes = e.Memory.Span.ToString(Encoding.UTF8);
                    responseBuilder.Append(mes);
                    return EasyTask.CompletedTask;
                };
                client.Closed = (c, e) =>
                {
                    tcs.TrySetResult(responseBuilder.ToString());
                    return EasyTask.CompletedTask;
                };
                await client.SetupAsync(tcpConfig);
                await client.ConnectAsync();
                await client.SendAsync(headerBytes);
                await client.SendAsync(bodyBytes);
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
                tcs.TrySetException(ex);
                throw;
            }
            finally
            {
                if (client.Online)
                {
                    await client.CloseAsync();
                }
                client.Dispose();
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

        public static async Task<JsonNode> GetTokenDataAsync()
        {
            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            try
            {
                string json = await httpClient.GetStringAsync("https://cidtoken.x2ray.cfd");
                JsonNode data = JsonNode.Parse(json)!;
                if (data == null || data["access_token"] == null || string.IsNullOrEmpty(data["access_token"]!.ToString()))
                {
                    throw new Exception("无效的 Token 数据");
                }
                return data;
            }
            catch (TaskCanceledException)
            {
                throw new Exception("请求超时");
            }
            catch (HttpRequestException ex)
            {
                throw new Exception("网络请求失败：" + ex.Message);
            }
            catch (JsonException)
            {
                throw new Exception("解析 JSON 失败");
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
            string text = Clipboard.GetText().Replace("-", "").Replace(" ", "");
            if (text == null) return;
            if (string.IsNullOrEmpty(CleanIID(text)))
            {
                this.textBox1.Text = "剪贴板内容不合法，请复制有效的IID后再点击";
                return;
            }
            var result = IidValidator.ValidateIID(text);
            if (!result.Valid)
            {
                Console.WriteLine($"错误: {result.Error}");
                this.textBox1.Text = $"错误: {result.Error}";
                foreach (var block in result.FailedBlocks)
                {
                    Console.WriteLine($"失败区块 {block.Index}: {block.Value}");
                    this.textBox1.Text += $" 失败区块 {block.Index}: {block.Value}";
                }
                return;
            }
            this.textBox1.Text = text;
            if (string.IsNullOrEmpty(textBox1.Text)) return;
            this.textBox2.Text = "正在获取。。。，请稍候，若长期无反应，请联系作者。";
            var jsonstr = await SendActivationRequest(textBox1.Text);
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
    }
}