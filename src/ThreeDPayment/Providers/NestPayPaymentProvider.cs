using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ThreeDPayment.Requests;
using ThreeDPayment.Results;

namespace ThreeDPayment.Providers
{
    public class NestPayPaymentProvider : IPaymentProvider
    {
        public Task<PaymentGatewayResult> ThreeDGatewayRequest(PaymentGatewayRequest request)
        {
            try
            {
                string clientId = request.BankParameters["clientId"];
                string processType = request.BankParameters["processType"];
                string storeKey = request.BankParameters["storeKey"];
                string storeType = request.BankParameters["storeType"];
                string totalAmount = request.TotalAmount.ToString(new CultureInfo("en-US"));
                string random = DateTime.Now.ToString();

                Dictionary<string, object> parameters = new Dictionary<string, object>();
                parameters.Add("clientid", clientId);
                parameters.Add("amount", totalAmount);//kuruş ayrımı nokta olmalı!!!
                parameters.Add("oid", request.OrderNumber);//sipariş numarası

                //işlem başarılı da olsa başarısız da olsa callback sayfasına yönlendirerek kendi tarafımızda işlem sonucunu kontrol ediyoruz
                parameters.Add("okUrl", request.CallbackUrl);//başarılı dönüş adresi
                parameters.Add("failUrl", request.CallbackUrl);//hatalı dönüş adresi
                parameters.Add("islemtipi", processType);//direk satış
                parameters.Add("rnd", random);//rastgele bir sayı üretilmesi isteniyor

                string installment = string.Empty;

                if (request.Installment > 1)
                {
                    installment = request.Installment.ToString();
                    parameters.Add("taksit", request.Installment);//taksit sayısı | 1 veya boş tek çekim olur
                }

                string hashstr = $"{clientId}{request.OrderNumber}{totalAmount}{request.CallbackUrl}{request.CallbackUrl}{processType}{installment}{random}{storeKey}";
                SHA1CryptoServiceProvider cryptoServiceProvider = new SHA1CryptoServiceProvider();
                byte[] inputbytes = cryptoServiceProvider.ComputeHash(Encoding.UTF8.GetBytes(hashstr));
                string hashData = Convert.ToBase64String(inputbytes);

                parameters.Add("hash", hashData);//hash data
                parameters.Add("currency", request.CurrencyIsoCode);//ISO code TL 949 | EURO 978 | Dolar 840

                if (!request.CommonPaymentPage)
                {
                    parameters.Add("pan", request.CardNumber);
                    parameters.Add("cardHolderName", request.CardHolderName);
                    parameters.Add("Ecom_Payment_Card_ExpDate_Month", request.ExpireMonth);//kart bitiş ay'ı
                    parameters.Add("Ecom_Payment_Card_ExpDate_Year", request.ExpireYear);//kart bitiş yıl'ı
                    parameters.Add("cv2", request.CvvCode);//kart güvenlik kodu
                    parameters.Add("cardType", "1");//kart tipi visa 1 | master 2 | amex 3
                }

                parameters.Add("storetype", storeType);
                parameters.Add("lang", request.LanguageIsoCode);//iki haneli dil iso kodu

                return Task.FromResult(PaymentGatewayResult.Successed(parameters, request.BankParameters["gatewayUrl"]));
            }
            catch (Exception ex)
            {
                return Task.FromResult(PaymentGatewayResult.Failed(ex.ToString()));
            }
        }

        public Task<VerifyGatewayResult> VerifyGateway(VerifyGatewayRequest request, PaymentGatewayRequest gatewayRequest, IFormCollection form)
        {
            if (form == null)
            {
                return Task.FromResult(VerifyGatewayResult.Failed("Form verisi alınamadı."));
            }

            string mdStatus = form["mdStatus"].ToString();
            if (string.IsNullOrEmpty(mdStatus))
            {
                return Task.FromResult(VerifyGatewayResult.Failed(form["mdErrorMsg"], form["ProcReturnCode"]));
            }

            string response = form["Response"].ToString();
            //mdstatus 1,2,3 veya 4 olursa 3D doğrulama geçildi anlamına geliyor
            if (!mdStatusCodes.Contains(mdStatus))
            {
                return Task.FromResult(VerifyGatewayResult.Failed($"{response} - {form["mdErrorMsg"]}", form["ProcReturnCode"]));
            }

            if (string.IsNullOrEmpty(response) || !response.Equals("Approved"))
            {
                return Task.FromResult(VerifyGatewayResult.Failed($"{response} - {form["ErrMsg"]}", form["ProcReturnCode"]));
            }

            StringBuilder hashBuilder = new StringBuilder();
            hashBuilder.Append(request.BankParameters["clientId"]);
            hashBuilder.Append(form["oid"].FirstOrDefault());
            hashBuilder.Append(form["AuthCode"].FirstOrDefault());
            hashBuilder.Append(form["ProcReturnCode"].FirstOrDefault());
            hashBuilder.Append(form["Response"].FirstOrDefault());
            hashBuilder.Append(form["mdStatus"].FirstOrDefault());
            hashBuilder.Append(form["cavv"].FirstOrDefault());
            hashBuilder.Append(form["eci"].FirstOrDefault());
            hashBuilder.Append(form["md"].FirstOrDefault());
            hashBuilder.Append(form["rnd"].FirstOrDefault());
            hashBuilder.Append(request.BankParameters["storeKey"]);

            SHA1CryptoServiceProvider cryptoServiceProvider = new SHA1CryptoServiceProvider();
            byte[] inputbytes = cryptoServiceProvider.ComputeHash(Encoding.UTF8.GetBytes(hashBuilder.ToString()));
            string hashData = Convert.ToBase64String(inputbytes);

            if (!form["HASH"].Equals(hashData))
            {
                return Task.FromResult(VerifyGatewayResult.Failed("Güvenlik imza doğrulaması geçersiz."));
            }

            int.TryParse(form["taksit"], out int installment);
            int.TryParse(form["EXTRA.HOSTMSG"], out int extraInstallment);

            return Task.FromResult(VerifyGatewayResult.Successed(form["TransId"], form["TransId"],
                installment, extraInstallment,
                response, form["ProcReturnCode"]));
        }

        public Dictionary<string, string> TestParameters => new Dictionary<string, string>
        {
            { "clientId", "700655000200" },
            { "processType", "Auth" },
            { "storeKey", "TRPS0200" },
            { "storeType", "3D_PAY" },
            { "gatewayUrl", "https://entegrasyon.asseco-see.com.tr/fim/est3Dgate" },
            { "userName", "ISBANKAPI" },
            { "password", "ISBANK07" },
            { "verifyUrl", "https://entegrasyon.asseco-see.com.tr/fim/api" }
        };

        private static readonly string[] mdStatusCodes = new[] { "1", "2", "3", "4" };
    }
}