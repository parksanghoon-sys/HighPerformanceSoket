using System;

namespace Hps.Broker
{
    /// <summary>
    /// Broker 계층에서 사용하는 stable logical subscriber id 이다.
    /// </summary>
    internal readonly struct SubscriberIdentity : IEquatable<SubscriberIdentity>
    {
        private SubscriberIdentity(string value)
        {
            Value = value;
        }

        /// <summary>
        /// protocol `REGISTER` command 에서 받은 identity token 원문이다.
        /// </summary>
        internal string Value { get; }

        /// <summary>
        /// identity token view 를 registry key 로 사용할 값 타입으로 변환한다.
        /// </summary>
        internal static SubscriberIdentity Create(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (value.Length == 0)
                throw new ArgumentException("Subscriber identity 는 비어 있을 수 없다.", nameof(value));

            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsWhiteSpace(value[index]))
                    throw new ArgumentException("Subscriber identity 는 공백 문자를 포함할 수 없다.", nameof(value));
            }

            return new SubscriberIdentity(value);
        }

        public bool Equals(SubscriberIdentity other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            if (obj is SubscriberIdentity)
                return Equals((SubscriberIdentity)obj);

            return false;
        }

        public override int GetHashCode()
        {
            if (Value == null)
                return 0;

            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value ?? string.Empty;
        }
    }
}
