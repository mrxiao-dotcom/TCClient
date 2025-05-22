namespace TCClient.Models
{
    public enum OrderSide
    {
        BUY,
        SELL
    }

    public enum OrderType
    {
        LIMIT,
        MARKET,
        STOP,
        STOP_MARKET,
        TAKE_PROFIT,
        TAKE_PROFIT_MARKET,
        TRAILING_STOP_MARKET
    }

    public enum TimeInForce
    {
        GTC,  // Good Till Cancel
        IOC,  // Immediate or Cancel
        FOK,  // Fill or Kill
        GTX   // Good Till Crossing
    }

    public enum OrderStatus
    {
        NEW,
        PARTIALLY_FILLED,
        FILLED,
        CANCELED,
        REJECTED,
        EXPIRED
    }
} 