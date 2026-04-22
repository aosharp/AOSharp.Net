using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Core.Inventory;
using AOSharp.Core.UI;
using Newtonsoft.Json;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace AOSharp.Core.GMI
{
    public static class GMI
    {
        private const string BASE_URL = "http://aomarket.funcom.com";
        private const string AWESOMIUM_USER_AGENT = "Mozilla/5.0 (Windows; U; Windows NT 6.2; en-US) AppleWebKit/533.3 (KHTML, like Gecko) Chrome/12.0.702.0 Safari/533.3 Awesomium/1.6.5";

        internal static HttpClient _httpClient;

        private static Dictionary<int, Dictionary<int, MarketItemVisuals>> _itemVisualsCache = new Dictionary<int, Dictionary<int, MarketItemVisuals>>();

        public static HttpClient HttpClient 
        { 
            get { return GetOrCreateInstance(); } 
        }

        private static HttpClient GetOrCreateInstance()
        {
            if(_httpClient != null)
                return _httpClient;

            uint cookie1 = 0;
            uint cookie2 = 0;

            Client_t.GetCookies(ref cookie1, ref cookie2);

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", AWESOMIUM_USER_AGENT);
            _httpClient.DefaultRequestHeaders.Add("X-Anarchy-CharacterID", Game.ClientInst.ToString());
            _httpClient.DefaultRequestHeaders.Add("X-Anarchy-Cookie", string.Format("{0} {1}", cookie1, cookie2));
            _httpClient.DefaultRequestHeaders.Add("X-Anarchy-ServerID", Game.ServerId.ToString());

            return _httpClient;
        }

        public static void Deposit(Item item)
        {
            Deposit(0, new List<Item>() { item });
        }

        public static void Deposit(int credits)
        {
            Deposit(credits, new List<Item>());
        }

        public static void Deposit(int credits, List<Item> items)
        {
            Network.Send(new MarketSendMessage
            {
                Sender = DynelManager.LocalPlayer.Identity,
                Credits = credits,
                Items = items.Select(x => new MarketSendSlot() { Slot = x.Slot }).ToArray() 
            });
        }

        public async static Task<MarketInventory> GetInventory()
        {
            try
            {
                HttpResponseMessage response = await HttpClient.GetAsync($"{BASE_URL}/marketLIVE/inventory");

                if (!response.IsSuccessStatusCode)
                    return null;

                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(await response.Content.ReadAsStringAsync());

                HtmlNodeCollection inventoryItems = doc.DocumentNode.SelectNodes("//table[@id='inventoryTable']//tr[not(ancestor::thead)]");
                HtmlNode inventoryCash = doc.DocumentNode.SelectSingleNode("//p[@id='inventoryCash']");

                List<MyMarketInventoryItem> items = inventoryItems.Where(x => x.Attributes.Contains("cluster_id")).Select(x => new MyMarketInventoryItem
                {
                    ClusterId = int.Parse(x.Attributes["cluster_id"].Value),
                    TemplateQL = int.Parse(x.Attributes["template_ql"].Value),
                    Count = int.Parse(x.SelectSingleNode(".//td[@id='item_count']").InnerText),
                    Slot = int.Parse(x.Attributes["slot_num"].Value)
                }).ToList();

                List<MarketClusterItem> itemsMissingVisuals = items.Where(x => !_itemVisualsCache.ContainsKey(x.ClusterId) || !_itemVisualsCache[x.ClusterId].ContainsKey(x.TemplateQL)).Cast<MarketClusterItem>().ToList();

                if (itemsMissingVisuals.Any())
                    await GetItemVisualsBatch(itemsMissingVisuals);

                foreach (MarketInventoryItem item in items)
                {
                    item.Name = _itemVisualsCache[item.ClusterId][item.TemplateQL].Name;
                    item.Icon = _itemVisualsCache[item.ClusterId][item.TemplateQL].Icon;
                }

                return new MarketInventory
                {
                    Credits = long.Parse(inventoryCash.InnerText),
                    Items = items
                };
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public async static Task<MyMarketOrders> GetMarketOrders()
        {
            try
            {
                HttpResponseMessage response = await HttpClient.GetAsync($"{BASE_URL}/marketLIVE/my_orders");

                if (!response.IsSuccessStatusCode)
                    return null;

                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(await response.Content.ReadAsStringAsync());

                HtmlNodeCollection sellOrdersTable = doc.DocumentNode.SelectNodes("//table[@id='sellOrderTable']//tr[not(ancestor::thead)]");
                HtmlNodeCollection buyOrdersTable = doc.DocumentNode.SelectNodes("//table[@id='buyOrderTable']//tr[not(ancestor::thead)]");

                List<MyMarketSellOrder> sellOrders = sellOrdersTable == null ? new List<MyMarketSellOrder>() : sellOrdersTable.Select(x => new MyMarketSellOrder
                {
                    Id = int.Parse(x.Attributes["order_id"].Value),
                    ClusterId = int.Parse(x.Attributes["cluster_id"].Value),
                    TemplateQL = int.Parse(x.Attributes["template_ql"].Value),
                    Price = long.Parse(x.SelectSingleNode(".//td[@id='selling_price']").InnerText),
                    Count = int.Parse(x.SelectSingleNode(".//td[@id='item_count']").InnerText),
                }).ToList();


                List<MyMarketBuyOrder> buyOrders = buyOrdersTable == null ? new List<MyMarketBuyOrder>() : buyOrdersTable.Select(x => new MyMarketBuyOrder
                {
                    Id = int.Parse(x.Attributes["order_id"].Value),
                    ClusterId = int.Parse(x.Attributes["cluster_id"].Value),
                    TemplateQL = int.Parse(x.Attributes["template_ql"].Value),
                    Price = long.Parse(x.SelectSingleNode(".//td[@id='buying_price']").InnerText),
                    MinQl = int.Parse(x.SelectSingleNode(".//td[5]").InnerText),
                    MaxQl = int.Parse(x.SelectSingleNode(".//td[6]").InnerText),
                    //TimeRemaining = TimeSpan.Parse(x.SelectSingleNode(".//td[8]").InnerText),
                    Count = int.Parse(x.SelectSingleNode(".//td[@id='item_count']").InnerText),
                }).ToList();

                List<MarketClusterItem> itemsMissingVisuals = sellOrders.Where(x => !_itemVisualsCache.ContainsKey(x.ClusterId) || !_itemVisualsCache[x.ClusterId].ContainsKey(x.TemplateQL)).Cast<MarketClusterItem>().ToList();
                itemsMissingVisuals.AddRange(buyOrders.Where(x => !_itemVisualsCache.ContainsKey(x.ClusterId) || !_itemVisualsCache[x.ClusterId].ContainsKey(x.TemplateQL)).Cast<MarketClusterItem>().ToList());

                if (itemsMissingVisuals.Any())
                    await GetItemVisualsBatch(itemsMissingVisuals);

                foreach (MyMarketSellOrder order in sellOrders)
                {
                    order.Name = _itemVisualsCache[order.ClusterId][order.TemplateQL].Name;
                    order.Icon = _itemVisualsCache[order.ClusterId][order.TemplateQL].Icon;
                }

                foreach (MyMarketBuyOrder order in buyOrders)
                {
                    order.Name = _itemVisualsCache[order.ClusterId][order.TemplateQL].Name;
                    order.Icon = _itemVisualsCache[order.ClusterId][order.TemplateQL].Icon;
                }

                return new MyMarketOrders
                {
                    SellOrders = sellOrders,
                    BuyOrders = buyOrders
                };
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public async static Task<JsonActionOutputDTOBase> ModifySellOrder(int orderId, long price)
        {
            ModifyOrderDTO dto = new ModifyOrderDTO
            {
                OrderId = orderId,
                Price = price
            };

            return await JsonAction("do_modify_sell", dto);
        }

        public async static Task<JsonActionOutputDTOBase> ModifyBuyOrder(int orderId, long price)
        {
            ModifyOrderDTO dto = new ModifyOrderDTO
            {
                OrderId = orderId,
                Price = price
            };

            return await JsonAction("do_modify_buy", dto);
        }

        public async static Task<JsonActionOutputDTOBase> WithdrawItem(int slot, int count)
        {
            WithdrawItemDTO dto = new WithdrawItemDTO
            {
                Slot = slot,
                Count = count
            };

            return await JsonAction($"withdraw_item", dto);
        }

        public async static Task<JsonActionOutputDTOBase> WithdrawCash(long amount)
        {
            WithdrawCashDTO dto = new WithdrawCashDTO
            {
                Amount = amount
            };

            return await JsonAction($"withdraw_cash", dto);
        }

        public async static Task<JsonActionOutputDTOBase> CancelSellOrder(int orderId)
        {
            CancelOrderDTO dto = new CancelOrderDTO
            {
                OrderId = orderId
            };

            return await JsonAction($"do_cancel_sell", dto);
        }

        public async static Task<JsonActionOutputDTOBase> CancelBuyOrder(int orderId)
        {
            CancelOrderDTO dto = new CancelOrderDTO
            {
                OrderId = orderId
            };

            return await JsonAction($"do_cancel_buy", dto);
        }

        public async static Task<JsonActionOutputDTOBase> CreateBuyOrder(int clusterID, long unit_price, int count, int minQl, int maxQl, MarketOrderDuration duration)
        {
            CreateBuyOrderDTO dto = new CreateBuyOrderDTO
            {
                ClusterId = clusterID,
                Price = unit_price,
                Count = count,
                MinQl = minQl,
                MaxQl = maxQl,
                Duration = (int)duration
            };

            return await JsonAction("do_create_buy_order", dto);
        }

        public async static Task<JsonActionOutputDTOBase> CreateSellOrder(int clusterID, long unit_price, int count, int ql, MarketOrderDuration duration)
        {
            CreateSellOrderDTO dto = new CreateSellOrderDTO
            {
                ClusterId = clusterID,
                Price = unit_price,
                Count = count,
                Ql = ql,
                Duration = (int)duration
            };

            return await JsonAction("do_create_sell_order", dto);
        }

        public async static Task<JsonActionOutputDTOBase> JsonAction(string action, JsonActionInputDTOBase dto)
        {
            StringContent content = new StringContent(JsonConvert.SerializeObject(dto), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await HttpClient.PostAsync($"{BASE_URL}/marketLIVE/{action}", content);

            if (!response.IsSuccessStatusCode)
                return null;

            return JsonConvert.DeserializeObject<JsonActionOutputDTOBase>(await response.Content.ReadAsStringAsync());
        }

        public async static Task<Dictionary<int, Dictionary<int, MarketItemVisuals>>> GetItemVisualsBatch(List<MarketClusterItem> items)
        {
            GetItemVisualsBatchInputDTO req = new GetItemVisualsBatchInputDTO
            {
                Items = items
            };

            StringContent content = new StringContent(JsonConvert.SerializeObject(req), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await HttpClient.PostAsync($"{BASE_URL}/marketLIVE/get_item_visuals_batch", content);

            response.EnsureSuccessStatusCode();

            var itemVisuals = JsonConvert.DeserializeObject<Dictionary<int, Dictionary<int, MarketItemVisuals>>>(await response.Content.ReadAsStringAsync());

            foreach(int clusterId in itemVisuals.Keys)
            {
                if (!_itemVisualsCache.ContainsKey(clusterId))
                    _itemVisualsCache[clusterId] = new Dictionary<int, MarketItemVisuals>();

                foreach(int ql in itemVisuals[clusterId].Keys)
                    _itemVisualsCache[clusterId][ql] = itemVisuals[clusterId][ql];
            }

            return itemVisuals;
        }
    }

    public class MarketInventory
    {
        public long Credits;
        public List<MyMarketInventoryItem> Items;

        public bool Find(string name, out MyMarketInventoryItem item)
        {
            return (item = Items.FirstOrDefault(x => x.Name == name)) != null;
        }

        public async Task<JsonActionOutputDTOBase> WithdrawCash(long amount)
        {
            return await GMI.WithdrawCash(amount);
        }
    }

    public class MyMarketInventoryItem : MarketInventoryItem
    {
        public int Slot;

        public async Task<JsonActionOutputDTOBase> ListForSale(long price, int count, MarketOrderDuration duration)
        {
            return await GMI.CreateSellOrder(ClusterId, price, count, TemplateQL, duration);
        }

        public async Task<JsonActionOutputDTOBase> Withdraw(int count)
        {
            return await GMI.WithdrawItem(Slot, count);
        }
    }

    public class MarketInventoryItem : MarketClusterItem
    {
        public string Name;
        public int Count;
        public int Icon;
    }

    public class MyMarketOrders
    {
        public List<MyMarketBuyOrder> BuyOrders;
        public List<MyMarketSellOrder> SellOrders;
    }

    public class ItemOrders
    {
        public List<MarketBuyOrder> BuyOrders;
        public List<MarketSellOrder> SellOrders;
    }

    public class MyMarketBuyOrder: MarketBuyOrder
    {
        public async Task<JsonActionOutputDTOBase> Cancel()
        {
            return await GMI.CancelBuyOrder(Id);
        }

        public async Task<JsonActionOutputDTOBase> ModifyPrice(long price)
        {
            return await GMI.ModifyBuyOrder(Id, price);
        }
    }

    public class MyMarketSellOrder : MarketSellOrder
    {
        public async Task<JsonActionOutputDTOBase> Cancel()
        {
            return await GMI.CancelSellOrder(Id);
        }

        public async Task<JsonActionOutputDTOBase> ModifyPrice(long price)
        {
            return await GMI.ModifySellOrder(Id, price);
        }
    }

    public class MarketBuyOrder : MarketOrder
    {
        public int MinQl;
        public int MaxQl;
    }

    public class MarketSellOrder : MarketOrder
    {

    }

    public class MarketOrder : MarketInventoryItem
    {
        public int Id;
        public long Price;
        public string Owner;
        //public TimeSpan TimeRemaining;
    }


    public class MarketClusterItem
    {
        [JsonProperty("cluster_id")]
        public int ClusterId;

        [JsonProperty("template_ql")]
        public int TemplateQL;
    }

    public class GetItemVisualsBatchInputDTO
    {
        [JsonProperty("clusters")]
        public List<MarketClusterItem> Items;
    }

    public class ModifyOrderDTO : JsonActionInputDTOBase
    {
        [JsonProperty("order_id")]
        public int OrderId;

        [JsonProperty("unit_price")]
        public long Price;
    }

    public class CancelOrderDTO : JsonActionInputDTOBase
    {
        [JsonProperty("order_id")]
        public int OrderId;
    }

    public class CreateSellOrderDTO : CreateOrderDTO
    {
        [JsonProperty("template_ql")]
        public int Ql;
    }

    public class CreateBuyOrderDTO : CreateOrderDTO
    {
        [JsonProperty("ql_low")]
        public int MinQl;

        [JsonProperty("ql_high")]
        public int MaxQl;
    }

    public class CreateOrderDTO : JsonActionInputDTOBase
    {
        [JsonProperty("cluster_id")]
        public int ClusterId;

        [JsonProperty("unit_price")]
        public long Price;

        [JsonProperty("item_count")]
        public int Count;

        [JsonProperty("order_duration")]
        public int Duration;
    }

    public class WithdrawItemDTO : JsonActionInputDTOBase
    {
        [JsonProperty("item_count")]
        public int Count;

        [JsonProperty("slot_num")]
        public int Slot;
    }

    public class WithdrawCashDTO : JsonActionInputDTOBase
    {
        [JsonProperty("amount")]
        public long Amount;
    }

    public class JsonActionInputDTOBase
    {
    }

    public class JsonActionOutputDTOBase
    {
        [JsonProperty("status")]
        public bool Succeeded;

        [JsonProperty("message")]
        public string Message;
    }

    public class MarketItemVisuals
    {
        [JsonProperty("icon")]
        public int Icon;

        [JsonProperty("name")]
        public string Name;
    }

    public enum MarketOrderDuration
    {
        Days14 = 1209600,
        Days28 = Days14 * 2,
        Days42 = Days14 * 3,
        Days56 = Days14 * 4,
    }
}
