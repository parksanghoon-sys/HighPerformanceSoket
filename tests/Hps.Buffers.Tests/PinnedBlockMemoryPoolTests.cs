using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Xunit;

namespace Hps.Buffers.Tests
{
    public sealed class PinnedBlockMemoryPoolTests
    {
        // 최소 API 계약 테스트: Rent 는 BlockSize 길이의 블록을 돌려주고 RentedCount 를 증가시켜야 하며,
        // Return 은 같은 대여를 반납 처리해 누수 감지 카운트를 0으로 되돌려야 한다.
        [Fact]
        public void RentAndReturn_TrackRentedCountAndBlockSize()
        {
            PoolApi pool = PoolApi.Create(4096);

            Assert.Equal(4096, pool.BlockSize);
            Assert.Equal(0, pool.RentedCount);

            byte[] block = pool.Rent();

            Assert.Equal(4096, block.Length);
            Assert.Equal(1, pool.RentedCount);

            pool.Return(block);

            Assert.Equal(0, pool.RentedCount);
        }

        // 풀 재사용 계약 테스트: 반납된 블록은 다음 Rent 에서 재사용될 수 있어야 한다.
        // 이 동작은 반복 I/O에서 관리힙 할당을 계속 늘리지 않기 위한 최소 조건이다.
        [Fact]
        public void Rent_AfterReturn_ReusesReturnedBlock()
        {
            PoolApi pool = PoolApi.Create(128);
            byte[] first = pool.Rent();
            pool.Return(first);

            byte[] second = pool.Rent();

            Assert.Same(first, second);
            Assert.Equal(1, pool.RentedCount);
        }

        // 반환 방어 계약 테스트: 다른 크기의 배열이 섞이면 RIO/io_uring 등록 단위와 풀 불변식이 깨진다.
        // 따라서 Return 은 BlockSize 와 다른 배열을 받아들이지 않아야 하며 카운트도 오염시키면 안 된다.
        [Fact]
        public void Return_WhenBlockSizeDoesNotMatch_ThrowsAndKeepsRentedCount()
        {
            PoolApi pool = PoolApi.Create(64);

            ArgumentException exception = Assert.Throws<ArgumentException>(delegate()
            {
                pool.Return(new byte[63]);
            });

            Assert.Contains("BlockSize", exception.Message);
            Assert.Equal(0, pool.RentedCount);
        }

        // 생성자 계약 테스트: 0 이하 크기의 블록은 소켓 I/O 버퍼로 의미가 없고,
        // 이후 Rent 에서 빈 배열을 내보내면 상위 계층이 진행 불가능한 상태가 된다.
        [Fact]
        public void Constructor_WhenBlockSizeIsNotPositive_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                PoolApi.Create(0);
            });
        }

        private sealed class PoolApi
        {
            private const string TypeName = "Hps.Buffers.PinnedBlockMemoryPool, Hps.Buffers";
            private readonly object _instance;
            private readonly Type _type;

            private PoolApi(object instance, Type type)
            {
                _instance = instance;
                _type = type;
            }

            public int BlockSize => GetIntProperty("BlockSize");

            public int RentedCount => GetIntProperty("RentedCount");

            public static PoolApi Create(int blockSize)
            {
                Type type = RequireType();
                try
                {
                    object? instance = Activator.CreateInstance(type, new object[] { blockSize });
                    if (instance == null)
                        throw new InvalidOperationException(TypeName + " 인스턴스를 만들 수 있어야 한다.");

                    return new PoolApi(instance, type);
                }
                catch (TargetInvocationException ex)
                {
                    RethrowInner(ex);
                    throw;
                }
            }

            public byte[] Rent()
            {
                object? result = Invoke("Rent");
                if (result == null)
                    throw new InvalidOperationException("Rent 는 byte[] 블록을 반환해야 한다.");

                return (byte[])result;
            }

            public void Return(byte[] block)
            {
                Invoke("Return", block);
            }

            private int GetIntProperty(string propertyName)
            {
                PropertyInfo? property = _type.GetProperty(propertyName);
                if (property == null)
                    throw new InvalidOperationException(propertyName + " 속성이 존재해야 한다.");

                object? value = property.GetValue(_instance);
                if (!(value is int))
                    throw new InvalidOperationException(propertyName + " 속성은 int 값을 반환해야 한다.");

                return (int)value;
            }

            private object? Invoke(string methodName, params object[] args)
            {
                MethodInfo? method = _type.GetMethod(methodName);
                if (method == null)
                    throw new InvalidOperationException(methodName + " 메서드가 존재해야 한다.");

                try
                {
                    return method.Invoke(_instance, args);
                }
                catch (TargetInvocationException ex)
                {
                    RethrowInner(ex);
                    throw;
                }
            }

            private static Type RequireType()
            {
                Type? type = Type.GetType(TypeName);
                Assert.True(type != null, TypeName + " 타입이 존재해야 한다.");
                if (type == null)
                    throw new InvalidOperationException(TypeName + " 타입이 존재해야 한다.");

                return type;
            }

            private static void RethrowInner(TargetInvocationException ex)
            {
                if (ex.InnerException == null)
                    return;

                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        }
    }
}
