using CodeBucket.Bitbucket.Controllers;
using MonoTouch.Dialog;
using BitbucketSharp.Models;
using System.Collections.Generic;
using MonoTouch.UIKit;
using System.Linq;
using CodeBucket.Controllers;
using CodeFramework.Controllers;
using CodeFramework.Elements;

namespace CodeBucket.Bitbucket.Controllers.Teams
{
    public class TeamController : ListController<string>
    {
        public TeamController(bool push = true) 
            : base(push)
        {
            Title = "Teams";
            SearchPlaceholder = "Search Teams";
        }

        protected override List<string> GetData(bool force, int currentPage, out int nextPage)
        {
            var items = Application.Client.Account.GetPrivileges(force);
            nextPage = -1;
            var teams = items.Teams.Keys.OrderBy(a => a).ToList();

            //Remove the current user from the 'teams'
            teams.Remove(Application.Account.Username);
            return teams;
        }

        protected override Element CreateElement(string obj)
        {
            return new StyledElement(obj, () => NavigationController.PushViewController(new ProfileController(obj), true));
        }
    }
}