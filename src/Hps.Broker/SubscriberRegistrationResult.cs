namespace Hps.Broker
{
    /// <summary>
    /// stable subscriber REGISTER 처리 결과이다.
    /// </summary>
    internal enum SubscriberRegistrationResult
    {
        Registered = 1,
        AlreadyRegistered = 2,
        Rebound = 3,
        TargetAlreadyRegisteredWithDifferentIdentity = 4
    }
}
