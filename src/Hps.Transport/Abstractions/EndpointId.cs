using System;

namespace Hps.Transport
{
    /// <summary>
    /// Interface Server 가 TCP connection 과 UDP remote endpoint 를 같은 logical endpoint 로 추적하기 위한
    /// 안정적인 식별자이다.
    ///
    /// 값은 transport handle 의 객체 참조와 분리된다. TCP reconnect 또는 UDP remote 재등록을 나중에 같은
    /// logical subscriber 로 묶을 수 있게 하려면 subscription table 이 connection 객체 자체에만 의존해서는 안 된다.
    /// 0 이하 값은 "아직 발급되지 않음"과 섞일 수 있으므로 public 값으로 허용하지 않는다.
    /// </summary>
    public readonly struct EndpointId : IEquatable<EndpointId>
    {
        /// <summary>
        /// 양수 endpoint id 값을 가진 식별자를 만든다.
        /// </summary>
        /// <param name="value">Endpoint 를 구분하는 양수 값이다.</param>
        public EndpointId(long value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));

            Value = value;
        }

        /// <summary>
        /// Endpoint 를 구분하는 양수 값이다. 값의 발급 정책은 endpoint registry 가 책임진다.
        /// </summary>
        public long Value { get; }

        /// <summary>
        /// 같은 id 값이면 같은 logical endpoint 로 본다.
        /// </summary>
        public bool Equals(EndpointId other)
        {
            return Value == other.Value;
        }

        /// <summary>
        /// boxing 된 값과 비교할 때도 id 값 기준 동등성을 유지한다.
        /// </summary>
        public override bool Equals(object? obj)
        {
            if (obj is EndpointId other)
                return Equals(other);

            return false;
        }

        /// <summary>
        /// Dictionary/HashSet 에서 endpoint id 값 기준으로 bucket 을 고르게 선택하게 한다.
        /// </summary>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <summary>
        /// 로그와 진단 출력에서 transport 객체 참조 대신 endpoint id 값을 명확하게 보여준다.
        /// </summary>
        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(EndpointId left, EndpointId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(EndpointId left, EndpointId right)
        {
            return !left.Equals(right);
        }
    }
}
