using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyAbp.PrivateMessaging.Authorization;
using EasyAbp.PrivateMessaging.PrivateMessageNotifications;
using EasyAbp.PrivateMessaging.PrivateMessages.Dtos;
using EasyAbp.PrivateMessaging.Users;
using EasyAbp.PrivateMessaging.Users.Dtos;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Data;
using Volo.Abp.Users;

namespace EasyAbp.PrivateMessaging.PrivateMessages
{
    [Authorize]
    public class PrivateMessageAppService : ApplicationService, IPrivateMessageAppService
    {
        private readonly IDataFilter _dataFilter;
        private readonly IPmUserLookupService _pmUserLookupService;
        private readonly IPrivateMessageRepository _privateMessageRepository;
        private readonly IPrivateMessageNotificationManager _notificationManager;
        private readonly IPrivateMessageSenderSideManager _privateMessageSenderSideManager;
        private readonly IPrivateMessageReceiverSideManager _privateMessageReceiverSideManager;

        public PrivateMessageAppService(
            IDataFilter dataFilter,
            IPmUserLookupService pmUserLookupService,
            IPrivateMessageRepository privateMessageRepository,
            IPrivateMessageNotificationManager notificationManager,
            IPrivateMessageSenderSideManager privateMessageSenderSideManager,
            IPrivateMessageReceiverSideManager privateMessageReceiverSideManager)
        {
            _dataFilter = dataFilter;
            _pmUserLookupService = pmUserLookupService;
            _privateMessageRepository = privateMessageRepository;
            _notificationManager = notificationManager;
            _privateMessageSenderSideManager = privateMessageSenderSideManager;
            _privateMessageReceiverSideManager = privateMessageReceiverSideManager;
        }

        [Authorize(PrivateMessagingPermissions.PrivateMessages.Default)]
        public async Task<PrivateMessageDto> GetAsync(Guid id)
        {
            using (_dataFilter.Disable<ISoftDelete>())
            {
                var message = await _privateMessageRepository.GetAsync(id);

                await AuthorizationService.CheckAsync(message, PrivateMessagingPermissions.PrivateMessages.Default);

                return await MapToDtoAndLoadMoreInfosAsync(message);
            }
        }

        [Authorize(PrivateMessagingPermissions.PrivateMessages.Default)]
        public async Task<PagedResultDto<PrivateMessageDto>> GetListAsync(PagedResultRequestDto input)
        {
            var list = await _privateMessageReceiverSideManager.GetListAsync(CurrentUser.GetId(), input.SkipCount,
                input.MaxResultCount);

            var count = await _privateMessageReceiverSideManager.CountAsync(CurrentUser.GetId());

            return new PagedResultDto<PrivateMessageDto>(count, await MapToDtoAndLoadMoreInfosAsync(list));
        }

        [Authorize(PrivateMessagingPermissions.PrivateMessages.Default)]
        public async Task<PagedResultDto<PrivateMessageDto>> GetListUnreadAsync(PagedResultRequestDto input)
        {
            var list = await _privateMessageReceiverSideManager.GetListAsync(CurrentUser.GetId(), input.SkipCount,
                input.MaxResultCount, true);

            var count = await _privateMessageReceiverSideManager.CountAsync(CurrentUser.GetId(), true);

            return new PagedResultDto<PrivateMessageDto>(count, await MapToDtoAndLoadMoreInfosAsync(list));
        }

        [Authorize(PrivateMessagingPermissions.PrivateMessages.Default)]
        public async Task<PagedResultDto<PrivateMessageDto>> GetListSentAsync(PagedResultRequestDto input)
        {
            var list = await _privateMessageSenderSideManager.GetListAsync(CurrentUser.GetId(), input.SkipCount,
                input.MaxResultCount);

            var count = await _privateMessageSenderSideManager.CountAsync(CurrentUser.GetId());

            return new PagedResultDto<PrivateMessageDto>(count, await MapToDtoAndLoadMoreInfosAsync(list));
        }

        [Authorize(PrivateMessagingPermissions.PrivateMessages.Delete)]
        public async Task DeleteAsync(IEnumerable<Guid> ids)
        {
            var messageList = await _privateMessageRepository.GetListAsync(ids);
            
            foreach (var message in messageList)
            {
                await AuthorizationService.CheckAsync(message, PrivateMessagingPermissions.PrivateMessages.Delete);

                await _privateMessageReceiverSideManager.DeleteAsync(message);
            }
        }

        [Authorize(PrivateMessagingPermissions.PrivateMessages.SetRead)]
        public async Task SetReadAsync(IEnumerable<Guid> ids)
        {
            var messageList = await _privateMessageRepository.GetListAsync(ids);

            foreach (var message in messageList)
            {
                await AuthorizationService.CheckAsync(message, PrivateMessagingPermissions.PrivateMessages.SetRead);

                await _privateMessageReceiverSideManager.SetReadAsync(message);
            }
        }

        [Authorize(PrivateMessagingPermissions.PrivateMessages.Create)]
        public async Task<PrivateMessageDto> CreateAsync(CreateUpdatePrivateMessageDto input)
        {
            var toUser = await _pmUserLookupService.FindByUserNameAsync(input.ToUserName);

            var message = await _privateMessageSenderSideManager.CreateAsync(new PrivateMessage(GuidGenerator.Create(),
                CurrentTenant.Id, toUser.Id, input.Title, input.Content));

            await _notificationManager.CreateAsync(new PrivateMessageNotification(GuidGenerator.Create(),
                toUser.Id, message.Id, message.GetTitlePreview()));

            return await MapToDtoAndLoadMoreInfosAsync(message);
        }

        private async Task<IReadOnlyList<PrivateMessageDto>> MapToDtoAndLoadMoreInfosAsync(
            IReadOnlyList<PrivateMessage> entityList)
        {
            var dtoList = ObjectMapper.Map<IReadOnlyList<PrivateMessage>, IReadOnlyList<PrivateMessageDto>>(entityList);

            var toUserIds = dtoList.Select(dto => dto.ToUserId);
            var creatorIds = dtoList.Where(dto => dto.CreatorId.HasValue).Select(dto => dto.CreatorId.Value);
            
            var pmUserDtoDict =
                (await _pmUserLookupService.GetListByIdsAsync(toUserIds.Concat(creatorIds).Distinct())).ToDictionary(
                    user => user.Id, user => ObjectMapper.Map<PmUser, PmUserDto>(user));
            
            foreach (var dto in dtoList)
            {
                if (pmUserDtoDict.ContainsKey(dto.ToUserId))
                {
                    dto.ToUser = pmUserDtoDict[dto.ToUserId];
                }
                
                if (dto.CreatorId.HasValue && pmUserDtoDict.ContainsKey(dto.CreatorId.Value))
                {
                    dto.Creator = pmUserDtoDict[dto.CreatorId.Value];
                }
            }

            return dtoList;
        }

        private async Task<PrivateMessageDto> MapToDtoAndLoadMoreInfosAsync(PrivateMessage entity)
        {
            return (await MapToDtoAndLoadMoreInfosAsync(new[] {entity})).First();
        }
    }
}