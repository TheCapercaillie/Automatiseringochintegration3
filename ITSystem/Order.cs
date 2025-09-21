using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ITSystem
{
    public enum OrderStatus { Pending, InProgress, Completed, Failed }

    //public class Order
    //{
    //    public int Id { get; set; }
    //    public string Product { get; set; } = "";
    //    public int Quantity { get; set; }
    //    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    //    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    //}

    public class Order
    {
        public int Id { get; set; }
        public string Product { get; set; } = "";
        public int Quantity { get; set; }
        public OrderStatus Status { get; set; } = OrderStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int Progress { get; set; } = 0;
        public int? RuntimeSeconds { get; set; }
    }

    public class MachineRuntime
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public int RuntimeSeconds { get; set; }
    }
}
