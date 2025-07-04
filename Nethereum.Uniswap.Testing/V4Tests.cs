using Nethereum.Signer;
using Nethereum.Uniswap.Core.Permit2.ContractDefinition;
using Nethereum.Uniswap.Permit2;
using Nethereum.Uniswap.UniversalRouter;
using Nethereum.Uniswap.UniversalRouter.Commands;
using Nethereum.Uniswap.UniversalRouter.V4Actions;
using Nethereum.Uniswap.V4;
using Nethereum.Uniswap.V4.Contracts.PoolManager;
using Nethereum.Uniswap.V4.Mappers;
using Nethereum.Uniswap.V4.PositionManager;
using Nethereum.Uniswap.V4.StateView;
using Nethereum.Uniswap.V4.V4Quoter;
using Nethereum.Uniswap.V4.V4Quoter.ContractDefinition;
using Nethereum.Util;
using Nethereum.Web3.Accounts;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using PoolKey = Nethereum.Uniswap.V4.V4Quoter.ContractDefinition.PoolKey;

namespace Nethereum.Uniswap.Core.Tests
{
    public class V4Tests
    {
        private readonly ITestOutputHelper _output;

        private static string usdt = "0xc2132d05d31c914a87c6611c10748aeb04b58e8f";
        private static string url = "";

        private static string privateKey = "";

        private static Web3.Web3 web3 = new Web3.Web3(new Account(privateKey), url);

        private PoolManagerService poolManager = new PoolManagerService(web3, UniswapAddresses.PolygonPoolManagerV4);
        private StateViewService stateViewService = new StateViewService(web3, UniswapAddresses.PolygonStateViewV4);
        private PositionManagerService positionManager = new PositionManagerService(web3, UniswapAddresses.PolygonPositionManagerV4);
        private V4QuoterService v4Quoter = new V4QuoterService(web3, UniswapAddresses.PolygonQuoterV4);
        private UniversalRouterService universalRouter = new UniversalRouterService(web3, UniswapAddresses.PolygonUniversalRouter);

        public V4Tests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task ShouldQuoteAndSwapPOLForERC20()
        {
            var pool = new PoolKey()
            {
                Currency0 = AddressUtil.ZERO_ADDRESS,
                Currency1 = usdt,
                Fee = 500,
                TickSpacing = 10,
                Hooks = "0x0000000000000000000000000000000000000000"
            };

            var pathKeys = V4PathEncoder.EncodeMultihopExactInPath(new List<PoolKey> { pool }, AddressUtil.ZERO_ADDRESS);
            var amountIn = Web3.Web3.Convert.ToWei(0.1);

            var quoteExactParams = new QuoteExactParams()
            {
                Path = pathKeys,
                ExactAmount = amountIn,
                ExactCurrency = AddressUtil.ZERO_ADDRESS,

            };


            var quote = await v4Quoter.QuoteExactInputQueryAsync(quoteExactParams);
            var quoteAmount = Web3.Web3.Convert.FromWei(quote.AmountOut, 6); //usdc 6 decimals


            var v4ActionBuilder = new UniversalRouterV4ActionsBuilder();
            var swapExactInSingle = new SwapExactIn()
            {
                AmountIn = amountIn,
                AmountOutMinimum = quote.AmountOut,
                CurrencyIn = AddressUtil.ZERO_ADDRESS,
                Path = pathKeys.MapToActionV4(),
            };

            var settleAllAction = new SettleAll()
            {
                Currency = AddressUtil.ZERO_ADDRESS,
                Amount = amountIn
            };

            var takeAll = new TakeAll()
            {
                Currency = usdt,
                MinAmount = 0
            };

            v4ActionBuilder.AddCommand(swapExactInSingle);
            v4ActionBuilder.AddCommand(settleAllAction);
            v4ActionBuilder.AddCommand(takeAll);

            var routerBuilder = new UniversalRouterBuilder();
            routerBuilder.AddCommand(v4ActionBuilder.GetV4SwapCommand());

            var executeFunction = routerBuilder.GetExecuteFunction(amountIn);

            var receipt = await universalRouter.ExecuteRequestAndWaitForReceiptAsync(executeFunction);

        }

        [Fact]
        public async Task ShouldQuoteAndSwapUSDTToPOL()
        {
            var quoterPoolKey = new PoolKey()
            {
                Currency0 = "0x0000000000000000000000000000000000000000",
                Currency1 = usdt,
                Fee = 500,
                TickSpacing = 10,
                Hooks = "0x0000000000000000000000000000000000000000"
            };

            var pathKeys = V4PathEncoder.EncodeMultihopExactInPath(new List<PoolKey> { quoterPoolKey }, usdt);

            var amountIn = Web3.Web3.Convert.ToWei(0.1, 6);

            var quoteExactParams = new QuoteExactParams()
            {
                Path = pathKeys,
                ExactAmount = amountIn,
                ExactCurrency = usdt,
            };

            var quote = await v4Quoter.QuoteExactInputQueryAsync(quoteExactParams);
            var quoteAmount = Web3.Web3.Convert.FromWei(quote.AmountOut, 18);

            decimal slippage = 0.98m;
            BigInteger adjustedAmountOut = quote.AmountOut * (BigInteger)(slippage * 100) / 100;

            // Настройка Permit2
            var permit2Service = new Permit2Service(web3, UniswapAddresses.PolygonPermitV4);
            var erc20Service = new Nethereum.StandardTokenEIP20.StandardTokenService(web3, usdt);

            // Одобрение для Permit2 (максимальное значение uint256)
            var maxApproval = BigInteger.Parse("115792089237316195423570985008687907853269984665640564039457584007913129639935");
            var approveReceipt = await erc20Service.ApproveRequestAndWaitForReceiptAsync(UniswapAddresses.PolygonPermitV4, maxApproval);

            // Одобрение для UniversalRouter через Permit2
            var deadline = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600);
            var maxUint160 = BigInteger.Pow(2, 160) - 1;

            var v4ActionBuilder = new UniversalRouterV4ActionsBuilder();

            UniversalRouter.V4Actions.PoolKey actionPoolKey = new()
            {
                Currency0 = "0x0000000000000000000000000000000000000000",
                Currency1 = usdt,
                Fee = 500,
                TickSpacing = 10,
                Hooks = "0x0000000000000000000000000000000000000000"
            };

            var swapExactInSingle = new SwapExactInSingle()
            {
                PoolKey = actionPoolKey,
                AmountIn = amountIn,
                AmountOutMinimum = adjustedAmountOut,
                ZeroForOne = false, // Assuming we are swapping from USDT to POL
                HookData = new byte[] { 0x00 } // No hooks in this example
            };

            var settleAllAction = new SettleAll()
            {
                Currency = usdt,
                Amount = amountIn
            };

            var takeAll = new TakeAll()
            {
                Currency = "0x0000000000000000000000000000000000000000",
                MinAmount = adjustedAmountOut
            };

            v4ActionBuilder.AddCommand(swapExactInSingle);
            v4ActionBuilder.AddCommand(settleAllAction);
            v4ActionBuilder.AddCommand(takeAll);

            var routerBuilder = new UniversalRouterBuilder();

            var ethKey = new EthECKey(privateKey);

            var signedPermit = await CreatePermitAsync(
                web3,
                token: usdt,
                amount: amountIn,
                spender: UniswapAddresses.PolygonUniversalRouter,
                key: ethKey);

            var permit2Command = new Permit2PermitCommand()
            {
                Permit = signedPermit.PermitRequest,
                Signature = signedPermit.GetSignatureBytes()
            };

            routerBuilder.AddCommand(permit2Command);
            routerBuilder.AddCommand(v4ActionBuilder.GetV4SwapCommand());

            var executeFunction = routerBuilder.GetExecuteFunction(amountIn);

            var receipt = await universalRouter.ExecuteRequestAndWaitForReceiptAsync(executeFunction);
        }


        public async Task<SignedPermit2<PermitSingle>> CreatePermitAsync(
                Web3.Web3 web3,
                string token,
                BigInteger amount,
                string spender,
                EthECKey key)
        {
            var deadlineUnix = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600); // 10 мин

            var permit = new PermitSingle
            {
                Details = new PermitDetails
                {
                    Token = token,
                    Amount = amount,
                    Expiration = deadlineUnix,
                    Nonce = 0 // временно 0, правильное значение будет установлено внутри GetSinglePermitWithSignatureAsync
                },
                Spender = spender,
                SigDeadline = deadlineUnix
            };

            var permit2 = new Permit2Service(web3, UniswapAddresses.PolygonPermitV4);
            return await permit2.GetSinglePermitWithSignatureAsync(permit, key);
        }
    }
}
