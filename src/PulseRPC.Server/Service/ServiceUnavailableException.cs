namespace PulseRPC.Server;

public class ServiceUnavailableException(string message) : Exception(message);
