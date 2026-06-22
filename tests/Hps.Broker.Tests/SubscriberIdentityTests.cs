using System;
using System.Reflection;
using Xunit;

namespace Hps.Broker.Tests
{
    public sealed class SubscriberIdentityTests
    {
        // identity 타입 계약 테스트: stable subscriber id 는 Broker 내부 모델이어야 하고, handler/server wiring 전에 독립적으로 검증 가능해야 한다.
        // 타입이 없으면 후속 registry 테스트가 compile failure 로 끝나므로 먼저 reflection 기반 assertion failure 로 Red를 만든다.
        [Fact]
        public void SubscriberIdentity_Contract_Exists()
        {
            Type? type = Type.GetType("Hps.Broker.SubscriberIdentity, Hps.Broker");

            Assert.NotNull(type);
            Assert.True(type!.IsValueType);
            Assert.NotNull(type.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic));
            Assert.NotNull(type.GetProperty("Value", BindingFlags.Instance | BindingFlags.NonPublic));
        }

        // identity token 검증 테스트: registry key 는 장기 보관되는 문자열이므로 빈 값과 공백 포함 값을 거부해야 한다.
        // 공백을 허용하면 REGISTER command 의 token 경계와 routing identity 경계가 서로 달라진다.
        [Theory]
        [InlineData("")]
        [InlineData("device a")]
        [InlineData("device\t-a")]
        public void Create_WhenTokenIsInvalid_Throws(string value)
        {
            Assert.Throws<ArgumentException>(delegate { SubscriberIdentity.Create(value); });
        }

        // null identity 는 protocol decode 이후 handler 버그에 가까우므로 명시적인 ArgumentNullException 으로 드러내야 한다.
        [Fact]
        public void Create_WhenTokenIsNull_Throws()
        {
            Assert.Throws<ArgumentNullException>(delegate { SubscriberIdentity.Create(null!); });
        }

        // identity equality 테스트: 같은 ASCII token 은 같은 logical subscriber 로 취급해야 reconnect rebinding 이 동작한다.
        [Fact]
        public void Equals_WhenTokenMatches_ReturnsTrue()
        {
            SubscriberIdentity first = SubscriberIdentity.Create("device-a");
            SubscriberIdentity second = SubscriberIdentity.Create("device-a");

            Assert.Equal(first, second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
            Assert.Equal("device-a", first.Value);
        }
    }
}
