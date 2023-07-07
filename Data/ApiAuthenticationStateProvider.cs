using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
namespace DataAcquisitionServerAppWithWebPage.Data
{


    public class ApiAuthenticationStateProvider : AuthenticationStateProvider
    {
        public event Action AuthenticationStateChanged;
        public ClaimsPrincipal _user;
        public bool ifRefresh = false;
        public bool iflogout = false;

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            if (_user == null)
            {
                _user = new ClaimsPrincipal(new ClaimsIdentity());
            }

            return Task.FromResult(new AuthenticationState(_user));
        }

        public async Task MarkUserAsAuthenticated(ClaimsPrincipal user)
        {
            _user = user;
            var state = new AuthenticationState(_user);
            NotifyAuthenticationStateChanged(Task.FromResult(state));
            // 触发状态更改事件
            AuthenticationStateChanged?.Invoke();
        }

        public async Task Logout()
        {
            _user = new ClaimsPrincipal(new ClaimsIdentity());
            var state = new AuthenticationState(_user);
            NotifyAuthenticationStateChanged(Task.FromResult(state));


        }



    }

}
