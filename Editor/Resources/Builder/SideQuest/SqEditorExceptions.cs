using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Base exception class for api relevant exceptions thrown out ot the SqEditorAppApi
/// </summary>


namespace Banter.SDKEditor {
    public class SqEditorApiException : Exception
    {
        public SqEditorApiException(int httpCode) : base("Api Exception")
        {
            HttpCode = httpCode;
        }

        public SqEditorApiException(string message, Exception inner = null) : base(message, inner)
        {
        }

        public SqEditorApiException(int httpCode, string message, Exception inner = null) : base(message, inner)
        {
            HttpCode = httpCode;
        }

        public SqEditorApiException() { }

        /// <summary>
        /// When set, the HTTP status code that was returned which resulted in an exception
        /// </summary>
        public int? HttpCode { get; private set; }
    }

    /// <summary>
    /// Exception thrown when Unity reports that there were network problems
    /// </summary>
    public class SqEditorApiNetworkException : SqEditorApiException
    {
        public SqEditorApiNetworkException() { }
        public SqEditorApiNetworkException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Exception thrown when there are authorization or authentication related issues
    /// </summary>
    public class SqEditorApiAuthException : SqEditorApiException
    {
        public SqEditorApiAuthException(int httpCode) : base(httpCode) { }
        public SqEditorApiAuthException(string message, Exception inner = null) : base(message, inner) { }
        public SqEditorApiAuthException(int httpCode, string message, Exception inner = null) : base(httpCode, message, inner) { }
    }

    /// <summary>
    /// Exception raised when an object being created already exists on the server
    /// </summary>
    public class SqEditorAlreadyExistsException : SqEditorApiException
    {
        public SqEditorAlreadyExistsException(int httpCode) : base(httpCode) { }
        public SqEditorAlreadyExistsException(string message, Exception inner = null) : base(message, inner) { }
        public SqEditorAlreadyExistsException(int httpCode, string message, Exception inner = null) : base(httpCode, message, inner) { }
    }
}