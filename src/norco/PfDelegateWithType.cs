namespace norco;

public record PfDelegateWithType<T>(Type Type, T Delegate) where T : Delegate;
