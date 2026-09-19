using Refit;

namespace MockedMpesa.API.Services;

public interface ICallbackApi
{
    [Post("")]
    Task<string> SendStkCallback([Body] object callback);
    
    [Post("")]
    Task<string> SendB2CCallback([Body] object callback);
    
    [Post("")]
    Task<string> SendAccountBalanceCallback([Body] object callback);
    
    [Post("")]
    Task<string> SendReversalCallback([Body] object callback);
}