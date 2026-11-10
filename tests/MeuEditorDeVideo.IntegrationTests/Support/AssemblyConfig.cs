// Os testes de integração compartilham FFmpeg, ONNX Runtime, pastas temporárias e o logger global do Serilog:
// executá-los em sequência evita interferência entre cenários.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
