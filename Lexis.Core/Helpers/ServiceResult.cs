using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Helpers
{
    public class ServiceResult<T>
    {
        public bool IsSuccess { get; }
        public T? Data { get; }
        public string? ErrorMessage { get; }
        public string? ErrorCode { get; }

        private ServiceResult(bool isSuccess, T? data, string? errorMessage, string? errorCode)
        {
            IsSuccess = isSuccess;
            Data = data;
            ErrorMessage = errorMessage;
            ErrorCode = errorCode;
        }

        public static ServiceResult<T> Success(T data)
            => new(true, data, null, null);

        public static ServiceResult<T> Failure(string message, string? errorCode = null)
            => new(false, default, message, errorCode);
    }
}
