using Nethereum.Uniswap.V4.Contracts.PoolManager;
using Nethereum.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nethereum.Uniswap.V4.V4Quoter.ContractDefinition;
using PoolKey = Nethereum.Uniswap.V4.V4Quoter.ContractDefinition.PoolKey;
using Nethereum.Uniswap.UniversalRouter;
using Nethereum.Uniswap.V4.Mappers;
using Nethereum.Web3.Accounts;
using Xunit;
using Nethereum.Uniswap.V4.StateView;
using Nethereum.Uniswap.V4.PositionManager;
using Nethereum.Uniswap.V4.V4Quoter;
using Nethereum.Uniswap.V4;
using Nethereum.Uniswap.UniversalRouter.V4Actions;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.Contracts.Standards.ERC20;
using Nethereum.StandardTokenEIP20;
using System.Numerics;
using Nethereum.Signer;
using Nethereum.Uniswap.Permit2;
using Nethereum.Uniswap.Core.Permit2.ContractDefinition;
using Nethereum.Signer.Crypto;
using Nethereum.Uniswap.UniversalRouter.Commands;
using Xunit.Abstractions;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Uniswap.UniversalRouter.ContractDefinition;

namespace Nethereum.Uniswap.Core.Tests
{
    public class V4Tests
    {
        /*[
    "0x0000000000000000000000000000000000000000",
    "0x91D1e0b9f6975655A381c79fd6f1D118D1c5b958",
    "500",
    "10",
    "0x24F7c9ea6B5be5227caAeB61366b56052386eae4"
]*/
        private readonly ITestOutputHelper _output;

        public V4Tests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task ShouldQuoteAndSwapPOLForERC20()
        {
            //https://chainlist.org/chain/1301
            var url = "https://polygon-mainnet.infura.io/v3/627db106041c4d51951098854dfdba79";
            var privateKey = "0xdcb1b8fe4ba38bd1053619c99bb5c27078b9a18d3ac59708baa65cadb6a2c394";
            var web3 = new Web3.Web3(new Account(privateKey), url);
            var poolManager = new PoolManagerService(web3, UniswapAddresses.PolygonPoolManagerV4);

            var wmatic = "0x0d500b1d8e8ef31e21c99d1db9a6444d3adf1270";
            var usdt = "0xc2132d05d31c914a87c6611c10748aeb04b58e8f";

            var pool = new PoolKey()
            {
                Currency0 = AddressUtil.ZERO_ADDRESS,
                Currency1 = usdt,
                Fee = 500,
                TickSpacing = 10,
                Hooks = "0x0000000000000000000000000000000000000000"
            };


            var stateViewService = new StateViewService(web3, UniswapAddresses.PolygonStateViewV4);
            var positionManager = new PositionManagerService(web3, UniswapAddresses.PolygonPositionManagerV4);
            var v4Quoter = new V4QuoterService(web3, UniswapAddresses.PolygonQuoterV4);
            var universalRouter = new UniversalRouterService(web3, UniswapAddresses.PolygonUniversalRouter);

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

            //var wrap = new Wrap
            //{
            //    Amount = amountIn
            //};

            //v4ActionBuilder.AddCommand(wrap);
            v4ActionBuilder.AddCommand(swapExactInSingle);
            v4ActionBuilder.AddCommand(settleAllAction);
            v4ActionBuilder.AddCommand(takeAll);

            var routerBuilder = new UniversalRouterBuilder();
            routerBuilder.AddCommand(v4ActionBuilder.GetV4SwapCommand());

            var executeFunction = routerBuilder.GetExecuteFunction(amountIn);

            var receipt = await universalRouter.ExecuteRequestAndWaitForReceiptAsync(executeFunction);

        }

        [Fact]
        public async Task ShouldQuoteAndSwapPOLForERC20Out()
        {
            var url = "https://polygon-mainnet.infura.io/v3/627db106041c4d51951098854dfdba79";
            var privateKey = "0xdcb1b8fe4ba38bd1053619c99bb5c27078b9a18d3ac59708baa65cadb6a2c394";
            var web3 = new Web3.Web3(new Account(privateKey), url);

            var wmatic = "0x0d500b1d8e8ef31e21c99d1db9a6444d3adf1270";
            var usdt = "0xc2132d05d31c914a87c6611c10748aeb04b58e8f";
            var walletAddress = web3.TransactionManager.Account.Address;

            // Pool for QUOTER (uses native POL / ZERO_ADDRESS)
            var poolForQuote = new PoolKey
            {
                Currency0 = AddressUtil.ZERO_ADDRESS,
                Currency1 = usdt,
                Fee = 500,
                TickSpacing = 10,
                Hooks = "0x0000000000000000000000000000000000000000"
            };

            var quoter = new V4QuoterService(web3, UniswapAddresses.PolygonQuoterV4);
            var universalRouter = new UniversalRouterService(web3, UniswapAddresses.PolygonUniversalRouter);

            var desiredUsdt = Web3.Web3.Convert.ToWei(0.05, 6); // 0.2 USDT
            var quote = await quoter.QuoteExactOutputQueryAsync(new QuoteExactParams
            {
                Path = V4PathEncoder.EncodeMultihopExactOutPath(new List<PoolKey> { poolForQuote }, usdt),
                ExactAmount = desiredUsdt,
                ExactCurrency = usdt
            });

            var requiredMatic = quote.AmountIn;
            var slippageAdjustedAmount = requiredMatic + (requiredMatic / 200); // 0.5%

            // Pool for ROUTER (uses WMATIC)
            var poolForRouter = new PoolKey
            {
                Currency0 = wmatic,
                Currency1 = usdt,
                Fee = 500,
                TickSpacing = 10,
                Hooks = "0x0000000000000000000000000000000000000000"
            };

            var pathKeys = V4PathEncoder.EncodeMultihopExactOutPath(new List<PoolKey> { poolForQuote }, usdt);

            var v4ActionBuilder = new UniversalRouterV4ActionsBuilder();

            // 1. Wrap native POL into WMATIC
            //v4ActionBuilder.AddCommand(new Wrap
            //{
            //    Amount = slippageAdjustedAmount
            //});

            //v4ActionBuilder.AddCommand(new SettleAll
            //{
            //    Currency = AddressUtil.ZERO_ADDRESS,
            //    Amount = slippageAdjustedAmount
            //});

            // 2. Swap WMATIC → USDT
            v4ActionBuilder.AddCommand(new SwapExactOut
            {
                CurrencyOut = usdt,
                AmountOut = desiredUsdt,
                AmountInMaximum = slippageAdjustedAmount,
                Path = pathKeys.MapToActionV4()
            });

            // 3. Settle WMATIC (to pay pool)
            v4ActionBuilder.AddCommand(new SettleAll
            {
                Currency = AddressUtil.ZERO_ADDRESS,
                Amount = slippageAdjustedAmount
            });

            // 4. Take exact USDT
            v4ActionBuilder.AddCommand(new Take
            {
                Currency = usdt,
                Amount = desiredUsdt,
                Recipient = walletAddress
            });

            //// 5. Unwrap leftover WMATIC to POL
            //v4ActionBuilder.AddCommand(new Unwrap
            //{
            //    Amount = 0
            //});

            var routerBuilder = new UniversalRouterBuilder();
            routerBuilder.AddCommand(v4ActionBuilder.GetV4SwapCommand());

            var executeFunction = routerBuilder.GetExecuteFunction(slippageAdjustedAmount);
            executeFunction.AmountToSend = slippageAdjustedAmount;
            try
            {
                var receipt = await universalRouter.ExecuteRequestAndWaitForReceiptAsync(executeFunction);
                Console.WriteLine("SUCCESS: " + receipt.TransactionHash);
            }
            catch (SmartContractRevertException revertEx)
            {
                Console.WriteLine("Smart contract reverted: " + revertEx.RevertMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine("General exception: " + ex.ToString());
            }
        }

        [Fact]
        public async Task ShouldQuoteAndSwapUSDTToPOL()
        {
            var url = "https://polygon-mainnet.infura.io/v3/627db106041c4d51951098854dfdba79";
            var privateKey = "0xdcb1b8fe4ba38bd1053619c99bb5c27078b9a18d3ac59708baa65cadb6a2c394";
            var web3 = new Web3.Web3(new Account(privateKey), url);
            var ethKey = new EthECKey(privateKey);
            var walletAddress = web3.TransactionManager.Account.Address;

            var usdt = "0xc2132d05d31c914a87c6611c10748aeb04b58e8f";
            var amountIn = Web3.Web3.Convert.ToWei(0.1, 6); // 0.1 USDT

            // 🔐 Убедись, что Permit2 одобрен на уровне токена
            await EnsurePermit2AllowanceAsync(web3, usdt, walletAddress, amountIn);

            //var permit2 = new Permit2Service(web3, UniswapAddresses.PolygonPermitV4);
            //var deadline = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600);
            //var receiptApprove = await permit2.ApproveRequestAndWaitForReceiptAsync(
            //    usdt,
            //    UniswapAddresses.PolygonUniversalRouter,
            //    BigInteger.Pow(2, 160) - 1,
            //    deadline
            //);


            // ✍️ Сгенерировать PermitSingle (внутри подставится nonce)
            var signedPermit = await CreatePermitAsync(
                web3,
                token: usdt,
                amount: amountIn,
                spender: UniswapAddresses.PolygonUniversalRouter,
                key: ethKey);

            var pool = new PoolKey()
            {
                Currency0 = AddressUtil.ZERO_ADDRESS,
                Currency1 = usdt,
                Fee = 500,
                TickSpacing = 10,
                Hooks = "0x0000000000000000000000000000000000000000"
            };

            var pathKeys = V4PathEncoder.EncodeMultihopExactInPath(new List<PoolKey> { pool }, usdt);

            var quoter = new V4QuoterService(web3, UniswapAddresses.PolygonQuoterV4);
            var quote = await quoter.QuoteExactInputQueryAsync(new QuoteExactParams
            {
                Path = pathKeys,
                ExactAmount = amountIn,
                ExactCurrency = usdt
            });

            var v4ActionBuilder = new UniversalRouterV4ActionsBuilder();

            // 1. Swap USDT → POL
            v4ActionBuilder.AddCommand(new SwapExactIn
            {
                AmountIn = amountIn,
                AmountOutMinimum = quote.AmountOut,
                CurrencyIn = usdt,
                Path = pathKeys.MapToActionV4()
            });

            // 2. Передаем USDT в пул
            v4ActionBuilder.AddCommand(new SettleAll
            {
                Currency = usdt,
                Amount = amountIn
            });

            // 3. Забираем POL (native)
            //v4ActionBuilder.AddCommand(new TakeAll
            //{
            //    Currency = AddressUtil.ZERO_ADDRESS,
            //    MinAmount = 0
            //});

            v4ActionBuilder.AddCommand(new Unwrap
            {
                Amount = 0
            });

            var routerBuilder = new UniversalRouterBuilder();

            var permit2Command = new Permit2PermitCommand()
            {
                Permit = signedPermit.PermitRequest,
                Signature = signedPermit.GetSignatureBytes()
            };
            routerBuilder.AddCommand(permit2Command);

            routerBuilder.AddCommand(v4ActionBuilder.GetV4SwapCommand());



            var universalRouter = new UniversalRouterService(web3, UniswapAddresses.PolygonUniversalRouter);
            var executeFunction = routerBuilder.GetExecuteFunction(); // Ничего не шлём в value, потому что input = ERC20
                                                                      // Приводим к ExecuteFunctionBase, чтобы получить доступ к полям
            if (executeFunction is ExecuteFunctionBase typedFunction)
            {
                var commandsHex = "0x" + BitConverter.ToString(typedFunction.Commands).Replace("-", "").ToLower();
                _output.WriteLine($"commands (bytes):\n{commandsHex}\n");

                var inputsHexList = typedFunction.Inputs
                    .Select(input => "\"0x" + BitConverter.ToString(input).Replace("-", "").ToLower() + "\"")
                    .ToList();

                var inputsFormatted = "[\n  " + string.Join(",\n  ", inputsHexList) + "\n]";
                _output.WriteLine($"inputs (bytes[]):\n{inputsFormatted}\n");
            }

            try
            {
                var txHash = await universalRouter.ExecuteRequestAsync(executeFunction);
                Console.WriteLine($"Sent! Tx: {txHash}");
            }
            catch (SmartContractRevertException ex)
            {
                Console.WriteLine("Smart contract revert reason:");
                Console.WriteLine(ex.RevertMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error:");
                Console.WriteLine(ex.Message);
            }

            var receipt = await universalRouter.ExecuteRequestAndWaitForReceiptAsync(executeFunction);

            Console.WriteLine($"✅ Swap USDT → POL complete. Tx: {receipt.TransactionHash}");
        }

        public async Task EnsurePermit2AllowanceAsync(Web3.Web3 web3, string tokenAddress, string ownerAddress, BigInteger requiredAmount)
        {
            var erc20 = new StandardTokenService(web3, tokenAddress);

            var allowance = await erc20.AllowanceQueryAsync(ownerAddress, UniswapAddresses.PolygonPermitV4);
            if (allowance < requiredAmount)
            {
                Console.WriteLine($"⛽ Approving Permit2 to spend your {tokenAddress}...");
                var receipt = await erc20.ApproveRequestAndWaitForReceiptAsync(
                    UniswapAddresses.PolygonPermitV4,
                    BigInteger.Pow(2, 256) - 1
                );
                Console.WriteLine($"✅ Approved: {receipt.TransactionHash}");
            }
            else
            {
                Console.WriteLine("✔️ Permit2 already approved.");
            }
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
