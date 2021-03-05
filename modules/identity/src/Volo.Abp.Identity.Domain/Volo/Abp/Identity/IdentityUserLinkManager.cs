﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Services;
using Volo.Abp.MultiTenancy;

namespace Volo.Abp.Identity
{
    public class IdentityLinkUserManager : DomainService
    {
        protected IIdentityLinkUserRepository IdentityLinkUserRepository { get; }

        protected IdentityUserManager UserManager { get; }

        protected new ICurrentTenant CurrentTenant { get; }

        public IdentityLinkUserManager(IIdentityLinkUserRepository identityLinkUserRepository, IdentityUserManager userManager, ICurrentTenant currentTenant)
        {
            IdentityLinkUserRepository = identityLinkUserRepository;
            UserManager = userManager;
            CurrentTenant = currentTenant;
        }

        public async Task<List<IdentityLinkUser>> GetListAsync(IdentityLinkUserInfo linkUserInfo,
            bool includeIndirect = false, CancellationToken cancellationToken = default)
        {
            var users = await IdentityLinkUserRepository.GetListAsync(linkUserInfo, cancellationToken: cancellationToken);
            if (includeIndirect == false)
            {
                return users;
            }

            var userInfos = new List<IdentityLinkUserInfo>()
            {
                linkUserInfo
            };

            var allUsers = new List<IdentityLinkUser>();
            allUsers.AddRange(users);

            do
            {
                var nextUsers = new List<IdentityLinkUserInfo>();
                foreach (var user in users)
                {
                    if (userInfos.Any(x => x.TenantId != user.SourceTenantId || x.UserId != user.SourceUserId))
                    {
                        nextUsers.Add(new IdentityLinkUserInfo(user.SourceUserId, user.SourceTenantId));
                    }

                    if (userInfos.Any(x => x.TenantId != user.TargetTenantId || x.UserId != user.TargetUserId))
                    {
                        nextUsers.Add(new IdentityLinkUserInfo(user.TargetUserId, user.TargetTenantId));
                    }
                }

                users = new List<IdentityLinkUser>();
                foreach (var next in nextUsers)
                {
                    users.AddRange(await IdentityLinkUserRepository.GetListAsync(next, userInfos, cancellationToken: cancellationToken));
                }

                userInfos.AddRange(nextUsers);
                allUsers.AddRange(users);
            } while (users.Any());

            return allUsers;
        }

        public virtual async Task LinkAsync(IdentityLinkUserInfo sourceLinkUser, IdentityLinkUserInfo targetLinkUser)
        {
            if (sourceLinkUser.UserId == targetLinkUser.UserId && sourceLinkUser.TenantId == targetLinkUser.TenantId)
            {
                return;
            }

            if (await IsLinkedAsync(sourceLinkUser, targetLinkUser))
            {
                return;
            }

            using (CurrentTenant.Change(null))
            {
                var userLink = new IdentityLinkUser(
                    GuidGenerator.Create(),
                    sourceLinkUser,
                    targetLinkUser);
                await IdentityLinkUserRepository.InsertAsync(userLink, true);
            }
        }

        public virtual async Task<bool> IsLinkedAsync(IdentityLinkUserInfo sourceLinkUser, IdentityLinkUserInfo targetLinkUser)
        {
            using (CurrentTenant.Change(null))
            {
                return await IdentityLinkUserRepository.FindAsync(sourceLinkUser, targetLinkUser) != null;
            }
        }

        public virtual async Task UnlinkAsync(IdentityLinkUserInfo sourceLinkUser, IdentityLinkUserInfo targetLinkUser)
        {
            if (!await IsLinkedAsync(sourceLinkUser, targetLinkUser))
            {
                return;
            }

            using (CurrentTenant.Change(null))
            {
                var linkedUser = await IdentityLinkUserRepository.FindAsync(sourceLinkUser, targetLinkUser);
                if (linkedUser != null)
                {
                    await IdentityLinkUserRepository.DeleteAsync(linkedUser);
                }
            }
        }

        public virtual async Task<string> GenerateLinkTokenAsync(IdentityLinkUserInfo targetLinkUser)
        {
            using (CurrentTenant.Change(targetLinkUser.TenantId))
            {
                var user = await UserManager.GetByIdAsync(targetLinkUser.UserId);
                return await UserManager.GenerateUserTokenAsync(
                    user,
                    LinkUserTokenProvider.LinkUserTokenProviderName,
                    LinkUserTokenProvider.LinkUserTokenPurpose);
            }
        }

        public virtual async Task<bool> VerifyLinkTokenAsync(IdentityLinkUserInfo targetLinkUser, string token)
        {
            using (CurrentTenant.Change(targetLinkUser.TenantId))
            {
                var user = await UserManager.GetByIdAsync(targetLinkUser.UserId);
                return await UserManager.VerifyUserTokenAsync(
                    user,
                    LinkUserTokenProvider.LinkUserTokenProviderName,
                    LinkUserTokenProvider.LinkUserTokenPurpose,
                    token);
            }
        }
    }
}
