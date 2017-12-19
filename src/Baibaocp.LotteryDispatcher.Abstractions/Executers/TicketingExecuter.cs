﻿using Baibaocp.Core.Messages;
using Baibaocp.LotteryDispatcher.Abstractions;
using System.Collections.Generic;

namespace Baibaocp.LotteryDispatcher.Executers
{
    public class TicketingExecuter : Executer
    {
        internal TicketingExecuter(string ldpVenderId, string ldpOrderId, List<OrderingMessage> lvpOrders) : base(ldpVenderId)
        {
            LdpOrderId = ldpOrderId;
            LvpOrders = lvpOrders;
        }

        public string LdpOrderId { get; }

        public IReadOnlyList<OrderingMessage> LvpOrders { get; }
    }
}
