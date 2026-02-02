namespace DWX_ZMQ_gRPC_Connector.Models;

public record TickData(
    string Symbol,
    double Bid,
    double Ask,
    DateTime Timestamp
);