﻿using Baibaocp.Core;
using Baibaocp.Core.Messages;
using Baibaocp.LotteryDispatcher.Executers;
using Dapper;
using Fighting.Extensions.Messaging.Abstractions;
using Fighting.Storaging;
using Microsoft.Extensions.Logging;
using Pomelo.Data.MySql;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Baibaocp.LotteryDispatcher.Shanghai.Handlers
{
    public class ShanghaiTicketingExecuteHandler : ShanghaiExecuteHandler<TicketingExecuter>
    {
        private readonly IMessagePublisher _publisher;

        private readonly StorageOptions _storageOptions;

        private readonly ILogger<ShanghaiTicketingExecuteHandler> _logger;

        public ShanghaiTicketingExecuteHandler(ShanghaiDispatcherOptions options, StorageOptions storageOptions, ILoggerFactory loggerFactory, IMessagePublisher publisher) : base(options, loggerFactory, "102")
        {
            _logger = loggerFactory.CreateLogger<ShanghaiTicketingExecuteHandler>();
            _storageOptions = storageOptions;
            _publisher = publisher;
        }

        /// <summary>  
        /// zlib.net 解压函数
        /// </summary>  
        /// <param name="strSource">带解压数据源</param>  
        /// <returns>解压后的数据</returns>  
        public static string DeflateDecompress(string strSource)
        {
            byte[] Buffer = Convert.FromBase64String(strSource); // 解base64  
            using (MemoryStream intms = new MemoryStream(Buffer))
            {
                using (zlib.ZInputStream inZStream = new zlib.ZInputStream(intms))
                {
                    int size = short.MaxValue;
                    byte[] buffer = new byte[size];
                    using (MemoryStream ms = new MemoryStream())
                    {
                        while ((size = inZStream.read(buffer, 0, size)) != -1)
                        {
                            ms.Write(buffer, 0, size);
                        }
                        inZStream.Close();
                        return Encoding.UTF8.GetString(ms.ToArray(), 0, (int)ms.Length);
                    }
                }
            }
        }
        protected override string BuildRequest(TicketingExecuter executer)
        {
            string[] values = new string[]
            {
                    string.Format("OrderID={0}", executer.LdpOrderId)
            };
            return string.Join("_", values);
        }

        protected string GetOdds(string xml)
        {
            using (MySqlConnection connection = new MySqlConnection(_storageOptions.DefaultNameOrConnectionString))
            {
                XElement element = XElement.Parse(xml);
                IEnumerable<XElement> bills = element.Elements("bill");
                StringBuilder sb = new StringBuilder();
                foreach (var bill in bills)
                {
                    IEnumerable<XElement> matches = bill.Elements("match");
                    foreach (var match in matches)
                    {
                        string attr = $"20{match.Attribute("id").Value}";
                        DateTime date = DateTime.ParseExact(attr.Substring(0, 8), "yyyyMMdd", CultureInfo.CurrentCulture);
                        string @event = attr.Substring(8);
                        string id = $"{date.ToString("yyyyMMdd")}{(date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek)}{@event}";
                        var rateCount = connection.ExecuteScalar("SELECT `RqspfRateCount` FROM `BbcpZcEvents` WHERE `Id` = @Id", new { Id = id });
                        string odds = match.Value.Replace('=', '*').Replace(',', '#');
                        sb.Append($"{id}@{rateCount}|{odds}#^");
                    }
                }
                return sb.ToString();
            }
        }

        public override async Task<bool> HandleAsync(TicketingExecuter executer)
        {
            string xml = await Send(executer);
            XDocument document = XDocument.Parse(xml);

            _logger.LogTrace(document.ToString());

            string Status = document.Element("ActionResult").Element("xCode").Value;
            if (Status.Equals("1"))
            {
                string odds = document.Element("ActionResult").Element("xValue").Value.Split('_')[3];
                string oddsXml = DeflateDecompress(odds);

                foreach (var lvpOrder in executer.LvpOrders)
                {
                    await _publisher.Publish(RoutingkeyConsts.Tickets.Completed.Success, new TicketedMessage
                    {
                        LvpOrderId = lvpOrder.LvpOrderId,
                        LvpVenderId = lvpOrder.LvpVenderId,
                        LdpOrderId = executer.LdpOrderId,
                        LdpVenderId = executer.LdpVenderId,
                        LotteryId = lvpOrder.LotteryId,
                        LotteryPlayId = lvpOrder.LotteryPlayId,
                        Amount = lvpOrder.InvestAmount,
                        TicketOdds = GetOdds(oddsXml),
                        Status = OrderStatus.TicketDrawing
                    }, CancellationToken.None);
                }
                return true;
            }
            else if (Status.Equals("2003"))
            {
                foreach (var lvpOrder in executer.LvpOrders)
                {
                    await _publisher.Publish(RoutingkeyConsts.Tickets.Completed.Failure, new TicketedMessage
                    {
                        LvpOrderId = lvpOrder.LvpOrderId,
                        LvpVenderId = lvpOrder.LvpVenderId,
                        LdpOrderId = executer.LdpOrderId,
                        LdpVenderId = executer.LdpVenderId,
                        LotteryId = lvpOrder.LotteryId,
                        LotteryPlayId = lvpOrder.LotteryPlayId,
                        Amount = lvpOrder.InvestAmount,
                        Status = OrderStatus.TicketFailed
                    }, CancellationToken.None);
                }
                return true;
            }
            // TODO: Log here and notice to admin
            _logger.LogWarning("Response message {0}", document.ToString(SaveOptions.DisableFormatting | SaveOptions.OmitDuplicateNamespaces));
            return false;
        }
    }
}
