using Braintree;
using System;
using System.Linq;
using System.Web.Mvc;

namespace BraintreeDemo
{
    public class CheckoutsController : Controller
    {
        public IBraintreeConfiguration config = new BraintreeConfiguration();

        public static readonly TransactionStatus[] transactionSuccessStatuses = {
                                                                                    TransactionStatus.AUTHORIZED,
                                                                                    TransactionStatus.AUTHORIZING,
                                                                                    TransactionStatus.SETTLED,
                                                                                    TransactionStatus.SETTLING,
                                                                                    TransactionStatus.SETTLEMENT_CONFIRMED,
                                                                                    TransactionStatus.SETTLEMENT_PENDING,
                                                                                    TransactionStatus.SUBMITTED_FOR_SETTLEMENT
                                                                                };

        public ActionResult New()
        {
            var gateway = config.GetGateway();
            var clientToken = gateway.ClientToken.Generate();
            ViewBag.ClientToken = clientToken;
            return View();
        }

        public ActionResult Create()
        {
            var gateway = config.GetGateway();
            var nonce = Request["payment_method_nonce"];
            decimal amount;

            try
            {
                amount = Convert.ToDecimal(Request["amount"]);
            }
            catch (FormatException)
            {
                TempData["Flash"] = "Error: 81503: Amount is an invalid format.";
                return RedirectToAction("New");
            }

            // Create the customer
            var customerRequest = new CustomerRequest
            {
                FirstName = Request["firstName"],
                LastName = Request["lastName"],
                Email = Request["email"],
                Phone = Request["phone"]
            };
            Result<Customer> customerResult = gateway.Customer.Create(customerRequest);

            var customerId = customerResult.Target.Id;

            // Create the address
            var addressRequest = new AddressRequest
            {
                FirstName = Request["firstName"],
                LastName = Request["lastName"],
                StreetAddress = Request["address"],
                Locality = Request["city"],
                Region = Request["state"],
                PostalCode = Request["zip"],
                CountryName = Request["country"]
            };

            Result<Address> addressResult = gateway.Address.Create(customerId, addressRequest);

            // Create the payment
            PaymentMethodRequest paymentMethodRequest = new PaymentMethodRequest
            {
                CustomerId = customerId,
                PaymentMethodNonce = nonce,
                Options = new PaymentMethodOptionsRequest
                {
                    VerifyCard = true
                }
            };

            Result<PaymentMethod> paymentMethodResult = gateway.PaymentMethod.Create(paymentMethodRequest);

            if (paymentMethodResult.Errors != null && paymentMethodResult.Errors.Count > 0)
            {
                TempData["Flash"] = $"Error: {paymentMethodResult.Message}";
                return RedirectToAction("New");
            }

            CreditCardVerification verification = paymentMethodResult.CreditCardVerification;

            if (verification != null && verification.ProcessorResponseCode != "1000")
            {
                TempData["Flash"] = $"Error: {verification.ProcessorResponseCode}: {verification.ProcessorResponseText}";
                return RedirectToAction("New");
            }

            // Create the transaction
            var request = new TransactionRequest
            {
                Amount = amount,
                PaymentMethodToken = paymentMethodResult.Target.Token,
                Options = new TransactionOptionsRequest
                {
                    SubmitForSettlement = true
                }
            };

            Result<Transaction> result = gateway.Transaction.Sale(request);
            if (result.IsSuccess())
            {
                Transaction transaction = result.Target;
                return RedirectToAction("Show", new { id = transaction.Id });
            }
            else if (result.Transaction != null)
            {
                return RedirectToAction("Show", new { id = result.Transaction.Id });
            }
            else
            {
                string errorMessages = "";
                foreach (ValidationError error in result.Errors.DeepAll())
                {
                    errorMessages += "Error: " + (int)error.Code + " - " + error.Message + "\n";
                }
                TempData["Flash"] = errorMessages;
                return RedirectToAction("New");
            }

        }

        public ActionResult Show(String id)
        {
            var gateway = config.GetGateway();
            Transaction transaction = gateway.Transaction.Find(id);

            if (transactionSuccessStatuses.Contains(transaction.Status))
            {
                TempData["header"] = "Sweet Success!";
                TempData["icon"] = "success";
                TempData["message"] = "Your test transaction has been successfully processed. See the Braintree API response and try again.";
            }
            else
            {
                TempData["header"] = "Transaction Failed";
                TempData["icon"] = "fail";
                TempData["message"] = "Your test transaction has a status of " + transaction.Status + ". See the Braintree API response and try again.";
            };

            ViewBag.Transaction = transaction;
            return View();
        }
    }
}