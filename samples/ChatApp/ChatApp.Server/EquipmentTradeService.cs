// using System;
// using System.Collections.Generic;
// using System.Threading.Tasks;
// using Microsoft.Extensions.Logging;
//
// namespace ChatApp.Server;
//
// // 装备交易服务
// public class EquipmentTradeService {
//     private readonly string _currentServerId;
//     private readonly IPlayerRoutingService _routingService;
//     private readonly IDistributedTransactionCoordinator _transactionCoordinator;
//     private readonly IPlayerResourceManager _resourceManager;
//     private readonly IRpcService _rpcService;
//     private readonly ILogger _logger;
//
//     public EquipmentTradeService(
//         string currentServerId,
//         IPlayerRoutingService routingService,
//         IDistributedTransactionCoordinator transactionCoordinator,
//         IPlayerResourceManager resourceManager,
//         IRpcService rpcService,
//         ILogger logger) {
//
//         _currentServerId = currentServerId;
//         _routingService = routingService;
//         _transactionCoordinator = transactionCoordinator;
//         _resourceManager = resourceManager;
//         _rpcService = rpcService;
//         _logger = logger;
//
//         // 注册RPC端点
//         RegisterRpcEndpoints();
//     }
//
//     // 执行装备交易
//     public async Task<TradeResult> ExecuteTrade(
//         string sellerPlayerId, Guid sellerEquipmentId,
//         string buyerPlayerId, int goldAmount) {
//
//         string tradeId = Guid.NewGuid().ToString();
//         _logger.Info($"Starting trade {tradeId} between {sellerPlayerId} and {buyerPlayerId}");
//
//         try {
//             // 1. 查找两个玩家所在的服务器
//             string sellerServerId = await _routingService.LocatePlayer(sellerPlayerId);
//             string buyerServerId = await _routingService.LocatePlayer(buyerPlayerId);
//
//             // 2. 创建需要锁定的资源列表
//             var resources = new List<ResourceIdentifier> {
//                 new ResourceIdentifier {
//                     ServerId = sellerServerId,
//                     ResourceKey = $"player:{sellerPlayerId}:equipment:{sellerEquipmentId}"
//                 },
//                 new ResourceIdentifier {
//                     ServerId = buyerServerId,
//                     ResourceKey = $"player:{buyerPlayerId}:gold"
//                 }
//             };
//
//             // 3. 开始分布式事务
//             TransactionContext transactionContext = null;
//             try {
//                 transactionContext = await _transactionCoordinator.BeginTransactionAsync(tradeId, resources);
//
//                 // 4. 验证交易条件
//                 bool sellerVerified = false;
//                 bool buyerVerified = false;
//
//                 // 验证卖家装备
//                 if (sellerServerId == _currentServerId) {
//                     // 本地验证
//                     sellerVerified = await VerifySellerEquipment(sellerPlayerId, sellerEquipmentId);
//                 } else {
//                     // 远程验证
//                     sellerVerified = await _rpcService.CallRemoteAsync<bool>(
//                         sellerServerId,
//                         "VerifySellerEquipment",
//                         new VerifySellerRequest {
//                             PlayerId = sellerPlayerId,
//                             EquipmentId = sellerEquipmentId,
//                             TransactionId = tradeId
//                         });
//                 }
//
//                 // 验证买家金币
//                 if (buyerServerId == _currentServerId) {
//                     // 本地验证
//                     buyerVerified = await VerifyBuyerGold(buyerPlayerId, goldAmount);
//                 } else {
//                     // 远程验证
//                     buyerVerified = await _rpcService.CallRemoteAsync<bool>(
//                         buyerServerId,
//                         "VerifyBuyerGold",
//                         new VerifyBuyerRequest {
//                             PlayerId = buyerPlayerId,
//                             GoldAmount = goldAmount,
//                             TransactionId = tradeId
//                         });
//                 }
//
//                 // 如果任一验证失败，则回滚事务
//                 if (!sellerVerified || !buyerVerified) {
//                     await _transactionCoordinator.RollbackTransactionAsync(tradeId);
//
//                     return new TradeResult {
//                         Success = false,
//                         ErrorCode = !sellerVerified ?
//                             TradeErrorCode.SellerEquipmentNotAvailable :
//                             TradeErrorCode.BuyerInsufficientGold
//                     };
//                 }
//
//                 // 5. 执行交易操作
//                 // 从卖家移除装备
//                 if (sellerServerId == _currentServerId) {
//                     await RemoveEquipmentFromSeller(sellerPlayerId, sellerEquipmentId, tradeId);
//                 } else {
//                     await _rpcService.CallRemoteAsync<bool>(
//                         sellerServerId,
//                         "RemoveEquipmentFromSeller",
//                         new RemoveEquipmentRequest {
//                             PlayerId = sellerPlayerId,
//                             EquipmentId = sellerEquipmentId,
//                             TransactionId = tradeId
//                         });
//                 }
//
//                 // 从买家扣除金币
//                 if (buyerServerId == _currentServerId) {
//                     await DeductGoldFromBuyer(buyerPlayerId, goldAmount, tradeId);
//                 } else {
//                     await _rpcService.CallRemoteAsync<bool>(
//                         buyerServerId,
//                         "DeductGoldFromBuyer",
//                         new DeductGoldRequest {
//                             PlayerId = buyerPlayerId,
//                             GoldAmount = goldAmount,
//                             TransactionId = tradeId
//                         });
//                 }
//
//                 // 给买家添加装备
//                 if (buyerServerId == _currentServerId) {
//                     await AddEquipmentToBuyer(buyerPlayerId, sellerEquipmentId, tradeId);
//                 } else {
//                     await _rpcService.CallRemoteAsync<bool>(
//                         buyerServerId,
//                         "AddEquipmentToBuyer",
//                         new AddEquipmentRequest {
//                             PlayerId = buyerPlayerId,
//                             EquipmentId = sellerEquipmentId,
//                             TransactionId = tradeId
//                         });
//                 }
//
//                 // 给卖家添加金币
//                 if (sellerServerId == _currentServerId) {
//                     await AddGoldToSeller(sellerPlayerId, goldAmount, tradeId);
//                 } else {
//                     await _rpcService.CallRemoteAsync<bool>(
//                         sellerServerId,
//                         "AddGoldToSeller",
//                         new AddGoldRequest {
//                             PlayerId = sellerPlayerId,
//                             GoldAmount = goldAmount,
//                             TransactionId = tradeId
//                         });
//                 }
//
//                 // 6. 提交事务
//                 await _transactionCoordinator.CommitTransactionAsync(tradeId);
//
//                 return new TradeResult {
//                     Success = true,
//                     TradeId = tradeId
//                 };
//             }
//             catch (Exception ex) {
//                 // 7. 发生异常，回滚事务
//                 _logger.Error($"Trade failed: {ex.Message}");
//
//                 if (transactionContext != null) {
//                     await _transactionCoordinator.RollbackTransactionAsync(tradeId);
//                 }
//
//                 return new TradeResult {
//                     Success = false,
//                     ErrorCode = TradeErrorCode.ServerError,
//                     ErrorMessage = "Internal server error during trade"
//                 };
//             }
//         }
//         catch (PlayerNotFoundException ex) {
//             _logger.Warning($"Player not found: {ex.Message}");
//             return new TradeResult {
//                 Success = false,
//                 ErrorCode = TradeErrorCode.PlayerNotFound,
//                 ErrorMessage = ex.Message
//             };
//         }
//         catch (Exception ex) {
//             _logger.Error($"Unexpected error: {ex.Message}");
//             return new TradeResult {
//                 Success = false,
//                 ErrorCode = TradeErrorCode.ServerError,
//                 ErrorMessage = "Unexpected server error"
//             };
//         }
//     }
//
//     // 注册RPC端点
//     private void RegisterRpcEndpoints() {
//         _rpcService.RegisterEndpoint<VerifySellerRequest, bool>(
//             "VerifySellerEquipment", HandleVerifySellerEquipment);
//
//         _rpcService.RegisterEndpoint<VerifyBuyerRequest, bool>(
//             "VerifyBuyerGold", HandleVerifyBuyerGold);
//
//         _rpcService.RegisterEndpoint<RemoveEquipmentRequest, bool>(
//             "RemoveEquipmentFromSeller", HandleRemoveEquipment);
//
//         _rpcService.RegisterEndpoint<DeductGoldRequest, bool>(
//             "DeductGoldFromBuyer", HandleDeductGold);
//
//         _rpcService.RegisterEndpoint<AddEquipmentRequest, bool>(
//             "AddEquipmentToBuyer", HandleAddEquipment);
//
//         _rpcService.RegisterEndpoint<AddGoldRequest, bool>(
//             "AddGoldToSeller", HandleAddGold);
//     }
//
//     // 验证卖家装备
//     private async Task<bool> VerifySellerEquipment(string playerId, Guid equipmentId) {
//         // 实际实现：检查装备是否存在、属于该玩家、可交易等
//         return true;
//     }
//
//     // 验证买家金币
//     private async Task<bool> VerifyBuyerGold(string playerId, int goldAmount) {
//         // 实际实现：检查玩家金币是否足够
//         return true;
//     }
//
//     // 从卖家移除装备
//     private async Task<bool> RemoveEquipmentFromSeller(
//         string playerId, Guid equipmentId, string transactionId) {
//         // 实际实现：从玩家背包移除装备
//         return true;
//     }
//
//     // 从买家扣除金币
//     private async Task<bool> DeductGoldFromBuyer(
//         string playerId, int goldAmount, string transactionId) {
//         // 实际实现：扣除玩家金币
//         return true;
//     }
//
//     // 给买家添加装备
//     private async Task<bool> AddEquipmentToBuyer(
//         string playerId, Guid equipmentId, string transactionId) {
//         // 实际实现：添加装备到玩家背包
//         return true;
//     }
//
//     // 给卖家添加金币
//     private async Task<bool> AddGoldToSeller(
//         string playerId, int goldAmount, string transactionId) {
//         // 实际实现：添加金币到玩家账户
//         return true;
//     }
//
//     // RPC处理方法
//     private async Task<bool> HandleVerifySellerEquipment(VerifySellerRequest request) {
//         return await VerifySellerEquipment(request.PlayerId, request.EquipmentId);
//     }
//
//     private async Task<bool> HandleVerifyBuyerGold(VerifyBuyerRequest request) {
//         return await VerifyBuyerGold(request.PlayerId, request.GoldAmount);
//     }
//
//     private async Task<bool> HandleRemoveEquipment(RemoveEquipmentRequest request) {
//         return await RemoveEquipmentFromSeller(
//             request.PlayerId, request.EquipmentId, request.TransactionId);
//     }
//
//     private async Task<bool> HandleDeductGold(DeductGoldRequest request) {
//         return await DeductGoldFromBuyer(
//             request.PlayerId, request.GoldAmount, request.TransactionId);
//     }
//
//     private async Task<bool> HandleAddEquipment(AddEquipmentRequest request) {
//         return await AddEquipmentToBuyer(
//             request.PlayerId, request.EquipmentId, request.TransactionId);
//     }
//
//     private async Task<bool> HandleAddGold(AddGoldRequest request) {
//         return await AddGoldToSeller(
//             request.PlayerId, request.GoldAmount, request.TransactionId);
//     }
// }
//
// // 交易结果
// public class TradeResult {
//     public bool Success { get; set; }
//     public string TradeId { get; set; }
//     public TradeErrorCode ErrorCode { get; set; }
//     public string ErrorMessage { get; set; }
// }
//
// // 交易错误码
// public enum TradeErrorCode {
//     None = 0,
//     PlayerNotFound,
//     SellerEquipmentNotAvailable,
//     BuyerInsufficientGold,
//     ServerError
// }
//
// // RPC请求模型
// public class VerifySellerRequest {
//     public string PlayerId { get; set; }
//     public Guid EquipmentId { get; set; }
//     public string TransactionId { get; set; }
// }
//
// public class VerifyBuyerRequest {
//     public string PlayerId { get; set; }
//     public int GoldAmount { get; set; }
//     public string TransactionId { get; set; }
// }
//
// public class RemoveEquipmentRequest {
//     public string PlayerId { get; set; }
//     public Guid EquipmentId { get; set; }
//     public string TransactionId { get; set; }
// }
//
// public class DeductGoldRequest {
//     public string PlayerId { get; set; }
//     public int GoldAmount { get; set; }
//     public string TransactionId { get; set; }
// }
//
// public class AddEquipmentRequest {
//     public string PlayerId { get; set; }
//     public Guid EquipmentId { get; set; }
//     public string TransactionId { get; set; }
// }
//
// public class AddGoldRequest {
//     public string PlayerId { get; set; }
//     public int GoldAmount { get; set; }
//     public string TransactionId { get; set; }
// }
