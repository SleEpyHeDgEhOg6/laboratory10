namespace StockAnalyzer.Models
{
    public class Price
    {
        public int Id { get; set; }
        public int TickerId { get; set; }
        public decimal Value { get; set; }
        public DateTime Date { get; set; }
    }
}
