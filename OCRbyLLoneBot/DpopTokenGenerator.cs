using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OCRbyLLoneBot
{
    public static class DpopTokenGenerator
    {
        // 单例模式缓存密钥对
        private static ECDsa? _ecdsaKeyPair;

        /// <summary>
        /// Base64URL编码（符合JWT/DPoP标准，无填充、替换+/_）
        /// </summary>
        /// <param name="input">要编码的字节数组</param>
        /// <returns>Base64URL编码字符串</returns>
        private static string Base64UrlEncode(byte[] input)
        {
            // 标准Base64编码 → 替换为Base64URL格式 → 移除末尾填充符=
            return Convert.ToBase64String(input)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        /// <summary>
        /// Base64URL编码（字符串重载）
        /// </summary>
        /// <param name="input">要编码的字符串</param>
        /// <returns>Base64URL编码字符串</returns>
        private static string Base64UrlEncode(string input)
        {
            return Base64UrlEncode(Encoding.UTF8.GetBytes(input));
        }

        /// <summary>
        /// 生成/获取ECDSA P-256密钥对（单例模式）
        /// </summary>
        /// <returns>P-256曲线的ECDSA密钥对</returns>
        private static ECDsa GetOrCreateECDsaKeyPair()
        {
            if (_ecdsaKeyPair == null)
            {
                // 创建P-256（nistP256）曲线的ECDSA密钥对
                _ecdsaKeyPair = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            }
            return _ecdsaKeyPair;
        }

        /// <summary>
        /// 将ECDSA公钥导出为JWK格式（符合DPoP标准）
        /// </summary>
        /// <param name="ecdsa">ECDSA密钥对</param>
        /// <returns>JWK格式的JSON字符串</returns>
        private static string ExportPublicKeyToJwk(ECDsa ecdsa)
        {
            // 导出公钥参数
            ECParameters publicKeyParams = ecdsa.ExportParameters(false);

            // 构建JWK对象（符合RFC 7517标准）
            var jwk = new
            {
                kty = "EC",          // 密钥类型：椭圆曲线
                crv = "P-256",       // 曲线类型
                x = Base64UrlEncode(publicKeyParams.Q.X), // X坐标（Base64URL编码）
                y = Base64UrlEncode(publicKeyParams.Q.Y)  // Y坐标（Base64URL编码）
            };

            // 序列化为无缩进的JSON（符合JWT规范）
            return JsonSerializer.Serialize(jwk, new JsonSerializerOptions { WriteIndented = false });
        }

        /// <summary>
        /// 生成DPoP令牌核心方法
        /// </summary>
        /// <param name="htu">请求的目标URL（如/api/productActivation/validateIID）</param>
        /// <param name="htm">请求方法（如POST/GET）</param>
        /// <returns>完整的DPoP令牌字符串</returns>
        public static string GenerateDpopToken(string htu, string htm="POST")
        {
            // 步骤1：获取/生成ECDSA P-256密钥对
            ECDsa ecdsa = GetOrCreateECDsaKeyPair();

            // 步骤2：构建DPoP Header（JWT头部）
            var header = new
            {
                alg = "ES256",       // 签名算法：ECDSA + SHA-256
                typ = "dpop+jwt",    // 令牌类型：DPoP格式的JWT
                jwk = JsonSerializer.Deserialize<object>(ExportPublicKeyToJwk(ecdsa)) // 公钥JWK
            };
            string encodedHeader = Base64UrlEncode(JsonSerializer.Serialize(header, new JsonSerializerOptions { WriteIndented = false }));

            // 步骤3：构建DPoP Payload（JWT载荷）
            var payload = new
            {
                htu = htu,           // 请求URL
                htm = htm,           // 请求方法
                jti = Guid.NewGuid().ToString(), // 唯一ID（防止重放攻击）
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds() // 签发时间（秒级时间戳）
            };
            string encodedPayload = Base64UrlEncode(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false }));

            // 步骤4：拼接Header和Payload
            string headerAndPayload = $"{encodedHeader}.{encodedPayload}";
            byte[] dataToSign = Encoding.UTF8.GetBytes(headerAndPayload);

            // 步骤5：用私钥签名（ES256 = ECDSA + SHA256）
            byte[] signature = ecdsa.SignData(dataToSign, HashAlgorithmName.SHA256);

            // 步骤6：对签名进行Base64URL编码
            string encodedSignature = Base64UrlEncode(signature);

            // 步骤7：拼接完整的DPoP令牌
            return $"{encodedHeader}.{encodedPayload}.{encodedSignature}";
        }

        /// <summary>
        /// 测试生成DPoP令牌
        /// </summary>
        public static void TestGenerateDpopToken()
        {
            string dpopToken = GenerateDpopToken(
                "/api/productActivation/validateIID", // 请求URL
                "POST"                                // 请求方法
            );
            Console.WriteLine("生成的DPoP令牌：");
            Console.WriteLine(dpopToken);
        }
    }
}
