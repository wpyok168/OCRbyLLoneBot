using System.Collections.Concurrent;
using System.Net.WebSockets;
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
        }
        
        private async void CreateSocket()
        {
            
            await webSocket.SetupAsync(new TouchSocketConfig()
                  .ConfigureContainer(a =>
                  {
                      a.AddConsoleLogger();
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
            webSocket.Received  = async(c, e) =>
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

            //解析OCR消息，提取文本内容
            (StringBuilder, StringBuilder) ocrResult;
            if (!string.IsNullOrEmpty(msg.Echo))
            {
                ocrResult = GetOCRTextConet(jsonDocument);
                string echo = msg.Echo;
                msg = ocrResmsgmode.TryGetValue(echo, out var ocrrecMsgMode) ? ocrrecMsgMode : msg;
                ocrResmsgmode.TryRemove(echo, out _);
                await SendMsg(msg, ocrResult.Item1.ToString());
            }
            return;
        }
        private RecMsgMode GetRecMsgMode(JsonDocument recmsgdic)
        {
            RecMsgMode recmsgMode = new RecMsgMode() { RecMsgContent=""};
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
                                    recmsgMode.ImageUrl = url.GetString();
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
                        sendmsg = sendmsg.Replace("\r", "\\r").Replace("\n", "\\n");
                        string msg1 = $@"{{""action"":""send_private_msg"",""params"":{{""user_id"":{rec.UserID},""message"":""{sendmsg.ToString()}"",""auto_escape"":false}}, ""echo"":""""}}";

                        await SendServerMsg(rec.Self_ID.ToString(), msg1);
                    }
                    else //if (subtype.GetString().Equals("group") && !string.IsNullOrEmpty(msgtext))
                    {
                        //message_type":"group"，"sub_type":"normal" 群聊  "message_type":"private"，"sub_type":"group" 群私聊  "message_type":"private"，"sub_type":"friend" 好友
                        if (rec.Message_Type.Equals("private"))//群私聊消息
                        {
                            sendmsg = sendmsg.Replace("\r", "\\r").Replace("\n", "\\n");
                            string msg1 = $"{{\"action\":\"send_msg\",\"params\":{{\"message_type\":\"private\", \"user_id\":{rec.UserID},\"group_id\":{rec.GroupID}, \"message\":\"{sendmsg}\",\"auto_escape\":false}}, \"echo\":\"\"}}";

                            await SendServerMsg(rec.Self_ID.ToString(), msg1);
                        }
                        else if (rec.Message_Type.Equals("group"))//群消息
                        {
                            sendmsg = sendmsg.Replace("\r", "\\r").Replace("\n", "\\n") + $"\\r\\n[CQ:at,qq={rec.UserID}]";
                            string msg1 = $"{{\"action\":\"send_group_msg\",\"params\":{{\"group_id\":{rec.GroupID}, \"message\":\"{sendmsg}\",\"auto_escape\":false}}, \"echo\":\"\"}}";

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
    }
}
