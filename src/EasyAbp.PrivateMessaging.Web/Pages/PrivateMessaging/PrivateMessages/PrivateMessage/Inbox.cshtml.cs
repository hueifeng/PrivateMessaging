using System.Threading.Tasks;

namespace EasyAbp.PrivateMessaging.Web.Pages.PrivateMessaging.PrivateMessages.PrivateMessage
{
    public class InboxModel : PrivateMessagingPageModel
    {
        public virtual async Task OnGetAsync()
        {
            await Task.CompletedTask;
        }
    }
}
