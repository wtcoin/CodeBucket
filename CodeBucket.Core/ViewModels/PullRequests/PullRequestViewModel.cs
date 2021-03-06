﻿using System;
using System.Threading.Tasks;
using CodeBucket.Core.Services;
using System.Reactive;
using System.Reactive.Linq;
using CodeBucket.Core.ViewModels.Users;
using System.Linq;
using ReactiveUI;
using Splat;
using CodeBucket.Core.ViewModels.Comments;
using Humanizer;
using CodeBucket.Core.Utils;
using CodeBucket.Client;

namespace CodeBucket.Core.ViewModels.PullRequests
{
    public class PullRequestViewModel : BaseViewModel, ILoadableViewModel
    {
        private readonly ReactiveList<PullRequestComment> _comments = new ReactiveList<PullRequestComment>();
        private readonly IApplicationService _applicationService;

        public string Username { get; }

        public string Repository { get; }

        public int PullRequestId { get; }

        public IReadOnlyReactiveList<CommentItemViewModel> Comments { get; }

        public ReactiveCommand<Unit, Unit> LoadCommand { get; }

        private readonly ObservableAsPropertyHelper<bool> _open;
        public bool IsOpen => _open.Value;

        private readonly ObservableAsPropertyHelper<bool> _approved;
        public bool Approved => _approved.Value;

        private readonly ObservableAsPropertyHelper<string> _description;
        public string Description => _description.Value;

        private readonly ObservableAsPropertyHelper<int?> _approvalCount;
        public int? ApprovalCount => _approvalCount.Value;

        private readonly ObservableAsPropertyHelper<UserItemViewModel[]> _approvals;
        public UserItemViewModel[] Approvals => _approvals.Value;

        private int? _commentCount;
        public int? CommentCount
        {
            get { return _commentCount; }
            private set { this.RaiseAndSetIfChanged(ref _commentCount, value); }
        }

        private readonly ObservableAsPropertyHelper<int?> _participants;
        public int? ParticipantCount => _participants.Value;

        private PullRequest _pullRequest;
        public PullRequest PullRequest 
        { 
            get { return _pullRequest; }
            set { this.RaiseAndSetIfChanged(ref _pullRequest, value); }
        }

        public ReactiveCommand<Unit, Unit> MergeCommand { get; }

        public ReactiveCommand<Unit, PullRequest> RejectCommand { get; }

        public ReactiveCommand<Unit, Unit> ToggleApproveButton { get; }

        public ReactiveCommand<string, Unit> GoToUserCommand { get; }

        public ReactiveCommand<Unit, Unit> GoToCommitsCommand { get; }

        public ReactiveCommand<Unit, Unit> ShowMenuCommand { get; }

        public PullRequestViewModel(
            string username, string repository, PullRequest pullRequest,
            IMarkdownService markdownService = null, IApplicationService applicationService = null,
            IActionMenuService actionMenuService = null)
            : this(username, repository, pullRequest.Id, markdownService, applicationService)
        {
            PullRequest = pullRequest;
        }

        public PullRequestViewModel(
            string username, string repository, int pullRequestId,
            IMarkdownService markdownService = null, IApplicationService applicationService = null,
            IActionMenuService actionMenuService = null)
        {
            _applicationService = applicationService = applicationService ?? Locator.Current.GetService<IApplicationService>();
            markdownService = markdownService ?? Locator.Current.GetService<IMarkdownService>();
            actionMenuService = actionMenuService ?? Locator.Current.GetService<IActionMenuService>();

            Title = $"Pull Request #{pullRequestId}";
            Username = username;
            Repository = repository;
            PullRequestId = pullRequestId;

            Comments = _comments.CreateDerivedCollection(x =>
            {
                var name = x.User.DisplayName ?? x.User.Username ?? "Unknown";
                var avatar = new Avatar(x.User.Links?.Avatar?.Href);
                return new CommentItemViewModel(name, avatar, x.CreatedOn.Humanize(), x.Content.Html);
            });

            Comments.Changed.Subscribe(_ => CommentCount = Comments.Count);

            LoadCommand = ReactiveCommand.CreateFromTask(async _ =>
            {
                PullRequest = await applicationService.Client.PullRequests.Get(username, repository, pullRequestId);
                _comments.Clear();
                await applicationService
                    .Client.ForAllItems(
                        x => x.PullRequests.GetComments(username, repository, pullRequestId), 
                         y =>
                         {
                             var items = y.Where(x => !string.IsNullOrEmpty(x.Content.Raw) && x.Inline == null)
                                          .OrderBy(x => (x.CreatedOn));
                             _comments.Reset(items);
                         });
            });

            GoToCommitsCommand = ReactiveCommand.CreateFromTask(t =>
            {
                if (PullRequest?.Source?.Repository == null)
                {
                    throw new Exception("The author has deleted the source repository for this pull request.");
                }

                var viewModel = new PullRequestCommitsViewModel(username, repository, pullRequestId);
                NavigateTo(viewModel);
                return Task.FromResult(Unit.Default);
            });

            var canMerge = this.WhenAnyValue(x => x.PullRequest)
                               .Select(x => string.Equals(x?.State, "open", StringComparison.OrdinalIgnoreCase));

            canMerge.ToProperty(this, x => x.IsOpen, out _open);

            MergeCommand = ReactiveCommand.Create(() => { }, canMerge);

            RejectCommand = ReactiveCommand.CreateFromTask(
                t => applicationService.Client.PullRequests.Decline(username, repository, pullRequestId),
                canMerge);

            RejectCommand.Subscribe(x => PullRequest = x);

            GoToUserCommand = ReactiveCommand.Create<string>(user => NavigateTo(new UserViewModel(user)));

            ToggleApproveButton = ReactiveCommand.CreateFromTask(async _ =>
            {
                if (Approved)
                    await applicationService.Client.PullRequests.Unapprove(username, repository, pullRequestId);
                else
                    await applicationService.Client.PullRequests.Approve(username, repository, pullRequestId);

                PullRequest = await applicationService.Client.PullRequests.Get(username, repository, pullRequestId);
            });

            //ToggleApproveButton.ThrownExceptions
            //    .Subscribe(x => DisplayAlert("Unable to approve commit: " + x.Message).ToBackground());


            var participantObs = this.WhenAnyValue(x => x.PullRequest.Participants)
                                     .Select(x => x ?? Enumerable.Empty<PullRequestParticipant>());

            var currentUsername = applicationService.Account.Username;

            participantObs
                .Select(x => x.FirstOrDefault(y => string.Equals(y.User.Username,
                    currentUsername, StringComparison.OrdinalIgnoreCase))?.Approved ?? false)
                .ToProperty(this, x => x.Approved, out _approved);

            participantObs
                .Select(x => new int?(x.Count()))
                .ToProperty(this, x => x.ParticipantCount, out _participants);

            participantObs
                .Select(x => new int?(x.Count(y => y.Approved)))
                .ToProperty(this, x => x.ApprovalCount, out _approvalCount);

            this.WhenAnyValue(x => x.PullRequest.Description)
                .Select(x => string.IsNullOrEmpty(x) ? null : markdownService.ConvertMarkdown(x))
                .ToProperty(this, x => x.Description, out _description);

            this.WhenAnyValue(x => x.PullRequest.Participants)
                .Select(participants =>
                {
                    return (participants ?? Enumerable.Empty<PullRequestParticipant>())
                        .Where(x => x.Approved)
                        .Select(x =>
                        {
                            var avatar = new Avatar(x.User?.Links?.Avatar?.Href);
                            var vm = new UserItemViewModel(x.User?.Username, x.User?.DisplayName, avatar);
                            vm.GoToCommand
                              .Select(_ => new UserViewModel(x.User))
                              .Subscribe(NavigateTo);
                            return vm;
                        });
                })
                .Select(x => x.ToArray())
                .ToProperty(this, x => x.Approvals, out _approvals, new UserItemViewModel[0]);

            ShowMenuCommand = ReactiveCommand.CreateFromTask(sender =>
            {
                var menu = actionMenuService.Create();
                menu.AddButton("Show in Bitbucket", _ =>
                {
                    NavigateTo(new WebBrowserViewModel(PullRequest?.Links?.Html?.Href));
                });
                return menu.Show(sender);
            });
        }

        public async Task AddComment(string text)
        {
            var oldComment = await _applicationService.Client.PullRequests.AddComment(
                Username, Repository, PullRequestId, text);

            var comment = await _applicationService.Client.PullRequests.GetComment(
                Username, Repository, PullRequestId, oldComment.CommentId);

            _comments.Add(comment);
        }
    }
}
