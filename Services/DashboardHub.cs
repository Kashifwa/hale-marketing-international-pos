using System;

namespace Hale_Marketing_International.Services
{
    public static class DashboardHub
    {
        public static event Action RefreshEvent;

        public static void Notify()
        {
            RefreshEvent?.Invoke();
        }
    }
}