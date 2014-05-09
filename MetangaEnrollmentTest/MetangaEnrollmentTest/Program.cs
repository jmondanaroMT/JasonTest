using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace MetangaEnrollmentTest
{
    [System.Runtime.Serialization.DataContract]
    public class TransparentPaymentResponse
    {
        [System.Runtime.Serialization.DataMember]
        public string ResponseType { get; set; }
        [System.Runtime.Serialization.DataMember]
        public string ResponseValue { get; set; }
    }


    class Program
    {
        static MetangaWS.EnrollmentClient _enrollmentClient;
        static string _user;
        static string _pass;
        static string _targetMetanga="QA";
        static string _tenant = "demo";
        static string _paymentbroker;
        static Int32 _backDate;
        static MetangaWS.Package[] _packages;
        enum RunParameters { ACCOUNTS, DOAUTOPAY };
        static Dictionary<RunParameters, bool> _runParameters;

        static void Initialize()
        {
            switch(_targetMetanga)
            {
                case "QA":
                    {
                        _enrollmentClient.Endpoint.Address = new System.ServiceModel.EndpointAddress(string.Format("https://{0}.mybillaccesstest.com/soapservice",_tenant));
                        _paymentbroker =  string.Format("https://{0}.mypayaccesstest.com/paymentmethod/creditcard?",_tenant);
                        break;
                    }
                case "PROD":
                    {
                        _enrollmentClient.Endpoint.Address = new System.ServiceModel.EndpointAddress(string.Format("https://{0}.mybillaccess.com/soapservice", _tenant));
                        _paymentbroker = string.Format("https://{0}.mypaymentaccess.com/paymentmethod/creditcard?",_tenant);
                        break;
                    }
                default:
                    {
                        _enrollmentClient.Endpoint.Address = new System.ServiceModel.EndpointAddress(string.Format("https://{0}.metratech.com/soapservice", _tenant));
                        _paymentbroker = string.Format("https://{0}.metratech.com/paymentmethod/creditcard?", _tenant);
                        break;
                    }
            }
        }

        static Guid GetMetangaSession()
        {
            return _enrollmentClient.CreateSession(_user, _pass);
        }

        static TransparentPaymentResponse CreatePaymentMethod()
        {
            var requestURI =
                _paymentbroker +
                "address1=" + System.Web.HttpUtility.UrlEncode("200 West Street") +
                "&address2=" + string.Empty +
                "&cardVerificationNumber=" + "123" +
                "&city=" + "Waltham" +
                "&country=" + "US" +
                "&creditCardNumber=" + "4111111111111111" +
                "&creditCardType=" + "Visa" +
                "&email=" + "garbage" + //+ System.Web.HttpUtility.UrlEncode("jmondanaro@metratech.com") +
                "&expirationDate=" + System.Web.HttpUtility.UrlEncode("10/2013") +
                "&firstname=" + "Jason" +
                "&lastname=" + "Test" +
                "&phoneNumber=" + "1234567890" +
                //"&postal=" + "99999" +
                "&state=" + "MA";


            HttpWebRequest request = WebRequest.Create(requestURI) as HttpWebRequest;
            request.Accept = "application/json";
            using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new Exception(String.Format(
                    "Server error (HTTP {0}: {1}).",
                    response.StatusCode,
                    response.StatusDescription));
                //var reader = new System.IO.StreamReader(response.GetResponseStream());
                //var result = reader.ReadToEnd();
                //System.Diagnostics.Debug.WriteLine(result);
                DataContractJsonSerializer jsonreader = new DataContractJsonSerializer(typeof(TransparentPaymentResponse));
                return jsonreader.ReadObject(response.GetResponseStream()) as TransparentPaymentResponse;

            }
        }

        
        static async Task<bool> EnrollSimpleAccount(Guid mySession, int packageNumber, Guid token)
        {
            var start = DateTime.Now;
            bool enrollmentSucceeded = false;
            var rng = new Random();

            var aPackage = _packages[packageNumber].EntityId.GetValueOrDefault(Guid.Empty); 
                var anAccountIdentifier = String.Format("AAAPerfTest_{0}_{1}", DateTime.UtcNow.ToString("s"), Guid.NewGuid().ToString("N"));
                var anAccount = new MetangaWS.SampleAccount()
                {
                    ExternalId = anAccountIdentifier,
                    Name = new Dictionary<string, string>() { { "en-us", anAccountIdentifier } },
                    FirstName = "Jason",
                    LastName = "Enrollmenttest",
                    Currency = "USD",
                    Email = String.Format("{0}@metanga.com", anAccountIdentifier),
                    State = "MA",
                    Zip = "99999",
                    PaysAutomatically = false,
                    BillingCycleEndDate = (_backDate == 0) ? DateTime.Today.AddDays(-1) : DateTime.Today.AddDays(-rng.Next(_backDate * 30)),
                    BillingCycleUnit = "MO",
                    Country = "US",
                    Language = "en-us",
                    ExternalPayerId = anAccountIdentifier
                };

                if (token != Guid.Empty)
                {
                    anAccount.PaymentInstrumentId = token;
                    anAccount.PaysAutomatically = true;
                }

                try
                {
                    enrollmentSucceeded = false;
                    var invoice = await _enrollmentClient.EnrollNewAccountAsync(mySession, anAccount, DateTime.UtcNow.Date, aPackage, "NewsDemo", "MO", new MetangaWS.ProductSubscriptionSettings[0]);
                    Debug.WriteLine("Invoice {0} = {1:0.00}", invoice.InvoiceNumber, invoice.InvoiceSalesAmount + invoice.InvoiceTaxAmount);
                    enrollmentSucceeded = true;
                }
                catch (System.ServiceModel.FaultException<MetangaWS.MetangaFault> fe)
                {
                    System.Diagnostics.Debug.WriteLine(fe.Detail.ErrorId);
                    System.Diagnostics.Debug.WriteLine(fe.Message);
                }
                catch (System.ServiceModel.FaultException fe)
                {
                    System.Diagnostics.Debug.WriteLine(fe.Message);
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine(e.GetType().Name);
                    System.Diagnostics.Debug.WriteLine(e.Message);
                    System.Diagnostics.Debug.WriteLine(e.StackTrace);
                }

                return enrollmentSucceeded;
        }

        static Guid GetNewPaymentToken()
        {
            //Use Transparent Payments API to register the payment method
            var PaymentMethodRegistration = CreatePaymentMethod();
            if (PaymentMethodRegistration.ResponseType.ToLower() != "success")
            {
                //Registration failed so exit
                System.Diagnostics.Debug.WriteLine(String.Format("Payment Method Registration Failed: {0}", PaymentMethodRegistration.ResponseValue));
                return Guid.Empty;
            }

            //Registration succeeded so collect the returned token for the payment method.
            return Guid.Parse(PaymentMethodRegistration.ResponseValue);
        }

        static void Main(string[] args)
        {
            if ((args.Length != 6) && (args.Length != 7))
            {
                Console.Out.WriteLine("Usage: MetangaEnrollemntTest TargetEnvironment(QA, PROD, DEV) TENANT NUMACCOUNTS USER PASS [RandomBackDate in maximum months]");
                return;
            }

            //API Client
            _enrollmentClient = new MetangaWS.EnrollmentClient();
            _targetMetanga = args[0];
            _tenant = args[1];
            int totalAccounts = int.Parse(args[2]);
            _user = args[3];
            _pass = args[4];

            _backDate = Int32.Parse(args[5]);
             //Setup where we are running
            Initialize();

            _packages = _enrollmentClient.GetPackagesByPromotionCodeAsync(GetMetangaSession(), "NewsDemo", DateTime.Now).Result;
            
            _runParameters = new Dictionary<RunParameters, bool>() { { RunParameters.DOAUTOPAY, bool.Parse(args[6]) } };

            PerformanceCounter cpuCounter;
            PerformanceCounter ramCounter;

            cpuCounter = new PerformanceCounter();

            cpuCounter.CategoryName = "Processor";
            cpuCounter.CounterName = "% Processor Time";
            cpuCounter.InstanceName = "_Total";
            cpuCounter.NextValue();
            ramCounter = new PerformanceCounter("Memory", "Available MBytes");

            var myTasks = new Task<bool>[totalAccounts];
            var myRand = new Random();
            var myTimer = new Stopwatch();
            myTimer.Start();
            int remainingAccounts = totalAccounts;
            while (remainingAccounts > 0)
            {
                if (cpuCounter.NextValue() < 90)
                {
                    var token = Guid.Empty;
                    if (_runParameters[RunParameters.DOAUTOPAY])
                    {
                        myTimer.Stop();
                        token = GetNewPaymentToken();
                        myTimer.Start();
                    }
                    var t = EnrollSimpleAccount(GetMetangaSession(), myRand.Next(0, _packages.Length-1), token);
                    t.ContinueWith((Task<bool> x) => { System.Diagnostics.Debug.WriteLine("Task {0} Completed at {1:0.0} seconds. Enrollment success = {2}", x.Id, myTimer.Elapsed.TotalSeconds, x.Result); });
                    myTasks[totalAccounts - remainingAccounts] = t;
                    remainingAccounts--;
                    Debug.WriteLine("Task {0} Started at {1:0.0} seconds. {2} accounts left,  CPU at {3}%", t.Id, myTimer.Elapsed.TotalSeconds, remainingAccounts, cpuCounter.NextValue());
                }
            }

            var sendTime = myTimer.ElapsedMilliseconds;
            try
            {
                Task.WaitAll(myTasks);
            }
            catch (AggregateException aggEx)
            {
                foreach (var ex in aggEx.InnerExceptions)
                {
                    Debug.Write("One of the tasks had an exception => ");
                    if (ex.GetType() == typeof(System.ServiceModel.FaultException))
                    {
                        var fex = ex as System.ServiceModel.FaultException;
                        Debug.WriteLine("Fault action from {0}", fex.Action);
                    }
                    else
                    {
                        Debug.WriteLine(ex.Message);
                        Debug.Write(ex.StackTrace);
                        Debug.WriteLine("\n");
                    }
                }
            }
            myTimer.Stop();
            int successes = 0;
            int failures = 0;

            foreach (var t in myTasks)
            {
                if (t.Result)
                    successes++;
                else
                    failures++;
            }
            Debug.WriteLine("API Statistics:");
            Debug.WriteLine("  {0:0.000}s Sending Time", sendTime/1000.0);
            Debug.WriteLine("  {0:0.000}s Total Time", myTimer.ElapsedMilliseconds/1000.0);
            Debug.WriteLine("  {0:0.000}Enrollments/second on average", (double)totalAccounts/(myTimer.ElapsedMilliseconds)*1000.0);
            Debug.WriteLine("  {0} successes", successes);
            Debug.WriteLine("  {0} failures", failures);
        }
    }
}
