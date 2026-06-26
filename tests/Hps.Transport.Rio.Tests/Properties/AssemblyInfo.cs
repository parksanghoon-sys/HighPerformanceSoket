using Xunit;

// RIO 테스트는 같은 process 안에서 Windows Registered I/O provider 와 native completion queue 를 직접 만진다.
// raw capability tests 와 transport loopback tests 를 병렬 실행하면 provider/CQ 관측 순서가 test runner scheduling 에 섞여
// live UDP completion 이 timeout 으로 보일 수 있으므로, 이 테스트 프로젝트는 native integration 경계를 직렬화한다.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
