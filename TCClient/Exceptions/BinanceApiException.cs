using System;

namespace TCClient.Exceptions
{
    public class BinanceApiException : Exception
    {
        public string ErrorType { get; }
        public string ErrorDetails { get; }

        public BinanceApiException(string message, string errorType, string errorDetails) 
            : base(message)
        {
            ErrorType = errorType;
            ErrorDetails = errorDetails;
        }
    }
} 