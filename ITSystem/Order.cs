using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ITSystem
{
    public enum OrderStatus { Pending, InProgress, Completed, Failed }

    public class Order
    {
        public int Id { get; set; }
        public string Product { get; set; } = "";
        public int Quantity { get; set; }
        public OrderStatus Status { get; set; } = OrderStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
