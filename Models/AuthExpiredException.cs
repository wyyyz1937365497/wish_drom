namespace wish_drom.Models
{
    /// <summary>
    /// 认证凭证过期异常 - 用于触发重新登录流程
    /// </summary>
    public class AuthExpiredException : Exception
    {
        public AuthExpiredException(string message) : base(message) { }

        public AuthExpiredException(string message, Exception inner) : base(message, inner) { }
    }
}
