using System.IO;
using System.Collections.Concurrent;
using MeuEditorDeVideo.Application.Export;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.ProjectEngine.Format;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.UnitTests.Export;

public sealed class RenderQueueServiceTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "med-queue-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // ------------------------------------------------------------ infraestrutura de teste

    /// <summary>Motor falso: o comportamento de cada chamada é definido pelo teste.</summary>
    private sealed class FakeEngine : IRenderEngine
    {
        public Func<RenderRequest, IProgress<RenderProgress>?, CancellationToken, Task<RenderResult>> Handler { get; set; } =
            (req, _, _) => Task.FromResult(Succeed(req));

        public ConcurrentQueue<string> Started { get; } = new();

        public IReadOnlyList<EncoderInfo> AvailableEncoders(VideoCodec codec) =>
            [new EncoderInfo("libx264", "x264 (software)", IsHardware: false)];

        public Task<RenderResult> RenderAsync(RenderRequest request, IProgress<RenderProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Started.Enqueue(request.OutputPath);
            return Handler(request, progress, cancellationToken);
        }

        public static RenderResult Succeed(RenderRequest req) =>
            new([req.OutputPath], "libx264", TimeSpan.FromMilliseconds(5), []);
    }

    private static RenderProgress Progress(double fraction) =>
        new(RenderStage.Encoding, fraction, 10, 100, 25, TimeSpan.FromSeconds(1), null, 1, 1);


    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public event EventHandler<AppSettings>? Changed
        {
            add { }
            remove { }
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private RenderQueueService Create(FakeEngine engine, string? root = null) =>
        new(engine, new DomainJson(), new FakeSettings(), root ?? _root, NullLogger<RenderQueueService>.Instance);

    private static Project NewProject() =>
        Project.CreateNew("P", new ProjectSettings(), StorageMode.Linked, DateTimeOffset.UtcNow);

    private string OutputPath(string name) => Path.Combine(_root, "saida", name + ".mp4");

    private static Task<RenderJob> EnqueueAsync(RenderQueueService service, Project project, string output, ExportSettings? settings = null) =>
        service.EnqueueAsync(project, new Dictionary<Guid, string>(), settings ?? new ExportSettings(), output, "Modelo", MediaTime.Zero, null);

    // ------------------------------------------------------------ execução

    [Fact]
    public async Task Enqueued_job_completes_with_files_encoder_and_progress()
    {
        var engine = new FakeEngine
        {
            Handler = async (req, progress, ct) =>
            {
                progress?.Report(Progress(0.5));
                await Task.Yield();
                return FakeEngine.Succeed(req);
            },
        };
        using var service = Create(engine);
        await service.InitializeAsync();
        var progressEvents = 0;
        service.ProgressChanged += (_, _) => Interlocked.Increment(ref progressEvents);
        var project = NewProject();
        var output = OutputPath("ok");

        var job = await EnqueueAsync(service, project, output);
        var finished = await service.WhenFinishedAsync(job.Id).WaitAsync(Wait);

        Assert.Equal(RenderJobStatus.Completed, finished.Status);
        Assert.Equal(new[] { output }, finished.Files);
        Assert.Equal("libx264", finished.Encoder);
        Assert.True(progressEvents > 0);
        Assert.NotNull(service.ProgressOf(job.Id));
    }

    [Fact]
    public async Task Job_folder_keeps_a_snapshot_of_the_project()
    {
        using var service = Create(new FakeEngine());
        await service.InitializeAsync();
        var project = NewProject();

        var job = await EnqueueAsync(service, project, OutputPath("snap"));
        await service.WhenFinishedAsync(job.Id).WaitAsync(Wait);

        var json = File.ReadAllText(Path.Combine(_root, job.Id.ToString("N"), "project.json"));
        var snapshot = new DomainJson().Deserialize<Project>(json);
        Assert.NotNull(snapshot);
        Assert.Equal(project.Id, snapshot.Id);
        Assert.Equal(project.Name, snapshot.Name);
    }

    [Fact]
    public async Task Engine_medexception_marks_job_failed_with_its_code_and_message()
    {
        var engine = new FakeEngine
        {
            Handler = (_, _, _) => throw new MedException(MedErrors.EncodeFailed("libx264", new IOException("disco"))),
        };
        using var service = Create(engine);
        await service.InitializeAsync();

        var job = await EnqueueAsync(service, NewProject(), OutputPath("falha"));
        var finished = await service.WhenFinishedAsync(job.Id).WaitAsync(Wait);

        Assert.Equal(RenderJobStatus.Failed, finished.Status);
        Assert.Equal("MED-3006", finished.ErrorCode);
        Assert.NotNull(finished.Error);
        Assert.Contains("falhou durante a exportação", finished.Error);
        Assert.True(finished.CanResume);
    }

    [Fact]
    public async Task Unexpected_exception_marks_job_failed_with_generic_error()
    {
        var engine = new FakeEngine { Handler = (_, _, _) => throw new InvalidOperationException("bug") };
        using var service = Create(engine);
        await service.InitializeAsync();

        var job = await EnqueueAsync(service, NewProject(), OutputPath("bug"));
        var finished = await service.WhenFinishedAsync(job.Id).WaitAsync(Wait);

        Assert.Equal(RenderJobStatus.Failed, finished.Status);
        Assert.Equal("MED-0000", finished.ErrorCode);
    }

    [Fact]
    public async Task Cancel_while_running_cancels_and_resume_runs_again_to_completion()
    {
        var calls = 0;
        var engine = new FakeEngine();
        engine.Handler = async (req, progress, ct) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                progress?.Report(Progress(0.1));
                await Task.Delay(Timeout.Infinite, ct);
            }
            return FakeEngine.Succeed(req);
        };
        using var service = Create(engine);
        await service.InitializeAsync();
        var job = await EnqueueAsync(service, NewProject(), OutputPath("cancela"));
        await WaitUntilAsync(() => engine.Started.Count > 0);

        service.Cancel(job.Id);
        var canceled = await service.WhenFinishedAsync(job.Id).WaitAsync(Wait);

        Assert.Equal(RenderJobStatus.Canceled, canceled.Status);
        Assert.True(canceled.CanResume);

        service.Resume(job.Id);
        var completed = await service.WhenFinishedAsync(job.Id).WaitAsync(Wait);

        Assert.Equal(RenderJobStatus.Completed, completed.Status);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Two_jobs_run_sequentially_in_enqueue_order()
    {
        using var service = Create(new FakeEngine());
        await service.InitializeAsync();
        var project = NewProject();
        var first = await EnqueueAsync(service, project, OutputPath("primeiro"));
        var second = await EnqueueAsync(service, project, OutputPath("segundo"));

        await service.WhenFinishedAsync(second.Id).WaitAsync(Wait);
        var finishedFirst = await service.WhenFinishedAsync(first.Id).WaitAsync(Wait);

        Assert.Equal(RenderJobStatus.Completed, finishedFirst.Status);
        Assert.Equal(new[] { OutputPath("primeiro"), OutputPath("segundo") }, service.Jobs.Select(j => j.OutputPath).ToArray());
    }

    [Fact]
    public async Task Sequential_execution_starts_the_first_job_before_the_second()
    {
        var gate = new TaskCompletionSource();
        var engine = new FakeEngine
        {
            Handler = async (req, _, ct) =>
            {
                if (req.OutputPath.Contains("primeiro")) await gate.Task.WaitAsync(ct);
                return FakeEngine.Succeed(req);
            },
        };
        using var service = Create(engine);
        await service.InitializeAsync();
        var project = NewProject();
        var first = await EnqueueAsync(service, project, OutputPath("primeiro"));
        var second = await EnqueueAsync(service, project, OutputPath("segundo"));

        await WaitUntilAsync(() => engine.Started.Count > 0);
        await Task.Delay(100);
        Assert.Single(engine.Started);
        Assert.Equal(RenderJobStatus.Queued, service.Jobs.Single(j => j.Id == second.Id).Status);

        gate.SetResult();
        await service.WhenFinishedAsync(first.Id).WaitAsync(Wait);
        await service.WhenFinishedAsync(second.Id).WaitAsync(Wait);
        Assert.Equal(2, engine.Started.Count);
    }

    // ------------------------------------------------------------ validação

    [Fact]
    public async Task Invalid_settings_are_rejected_at_enqueue_with_code_3008()
    {
        using var service = Create(new FakeEngine());
        await service.InitializeAsync();
        var settings = new ExportSettings { Container = ExportContainer.WebM, VideoCodec = VideoCodec.H264 };

        var ex = await Assert.ThrowsAsync<MedException>(() => EnqueueAsync(service, NewProject(), OutputPath("invalido"), settings));

        Assert.Equal("MED-3008", ex.Error.Code);
        Assert.Empty(service.Jobs);
    }

    // ------------------------------------------------------------ persistência

    [Fact]
    public async Task Finished_jobs_are_reloaded_by_a_new_service_on_the_same_root()
    {
        var first = Create(new FakeEngine());
        await first.InitializeAsync();
        var job = await EnqueueAsync(first, NewProject(), OutputPath("persistido"));
        await first.WhenFinishedAsync(job.Id).WaitAsync(Wait);
        first.Dispose();

        using var second = Create(new FakeEngine());
        await second.InitializeAsync();

        var reloaded = Assert.Single(second.Jobs);
        Assert.Equal(job.Id, reloaded.Id);
        Assert.Equal(RenderJobStatus.Completed, reloaded.Status);
        Assert.Equal(new[] { OutputPath("persistido") }, reloaded.Files);
        Assert.Equal("libx264", reloaded.Encoder);
    }

    [Fact]
    public async Task Job_interrupted_by_dispose_is_reloaded_as_interrupted_and_resumable()
    {
        var engine = new FakeEngine
        {
            Handler = async (_, _, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return null!;
            },
        };
        var first = Create(engine);
        await first.InitializeAsync();
        var job = await EnqueueAsync(first, NewProject(), OutputPath("interrompido"));
        await WaitUntilAsync(() => engine.Started.Count > 0);

        first.Dispose();

        using var second = Create(new FakeEngine());
        await second.InitializeAsync();
        var reloaded = second.Jobs.Single(j => j.Id == job.Id);
        Assert.Equal(RenderJobStatus.Interrupted, reloaded.Status);
        Assert.True(reloaded.CanResume);
    }

    [Fact]
    public async Task Running_job_persisted_on_disk_is_turned_into_interrupted_on_initialize()
    {
        var first = Create(new FakeEngine());
        await first.InitializeAsync();
        var job = await EnqueueAsync(first, NewProject(), OutputPath("travado"));
        await first.WhenFinishedAsync(job.Id).WaitAsync(Wait);
        first.Dispose();

        // Simula uma queda: o arquivo ficou com o trabalho marcado como "Em andamento".
        var queuePath = Path.Combine(_root, "queue.json");
        var text = File.ReadAllText(queuePath).Replace("\"completed\"", "\"running\"");
        File.WriteAllText(queuePath, text);

        using var second = Create(new FakeEngine());
        await second.InitializeAsync();

        var reloaded = second.Jobs.Single(j => j.Id == job.Id);
        Assert.Equal(RenderJobStatus.Interrupted, reloaded.Status);
    }

    // ------------------------------------------------------------ remoção

    [Fact]
    public async Task Remove_deletes_the_job_and_its_folder()
    {
        using var service = Create(new FakeEngine());
        await service.InitializeAsync();
        var job = await EnqueueAsync(service, NewProject(), OutputPath("remover"));
        await service.WhenFinishedAsync(job.Id).WaitAsync(Wait);
        var folder = Path.Combine(_root, job.Id.ToString("N"));
        Assert.True(Directory.Exists(folder));

        service.Remove(job.Id);

        Assert.Empty(service.Jobs);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task Clear_finished_removes_completed_failed_and_canceled_but_keeps_active_jobs()
    {
        var engine = new FakeEngine();
        engine.Handler = async (req, _, ct) =>
        {
            if (req.OutputPath.Contains("bloqueia")) await Task.Delay(Timeout.Infinite, ct);
            if (req.OutputPath.Contains("falha")) throw new MedException(MedErrors.EncodeFailed("libx264", new IOException("x")));
            return FakeEngine.Succeed(req);
        };
        using var service = Create(engine);
        await service.InitializeAsync();
        var project = NewProject();

        var ok = await EnqueueAsync(service, project, OutputPath("ok"));
        var failed = await EnqueueAsync(service, project, OutputPath("falha"));
        await service.WhenFinishedAsync(ok.Id).WaitAsync(Wait);
        await service.WhenFinishedAsync(failed.Id).WaitAsync(Wait);

        var blocking = await EnqueueAsync(service, project, OutputPath("bloqueia"));
        var canceled = await EnqueueAsync(service, project, OutputPath("cancelado"));
        await WaitUntilAsync(() => engine.Started.Count >= 3);
        service.Cancel(canceled.Id);
        var remaining = await EnqueueAsync(service, project, OutputPath("bloqueia-restante"));
        Assert.Equal(RenderJobStatus.Queued, service.Jobs.Single(j => j.Id == remaining.Id).Status);

        service.Cancel(blocking.Id);
        await service.WhenFinishedAsync(blocking.Id).WaitAsync(Wait);
        service.ClearFinished();

        var ids = service.Jobs.Select(j => j.Id).ToArray();
        Assert.DoesNotContain(ok.Id, ids);
        Assert.DoesNotContain(failed.Id, ids);
        Assert.DoesNotContain(canceled.Id, ids);
        Assert.DoesNotContain(blocking.Id, ids);
        Assert.Contains(remaining.Id, ids);
        Assert.False(Directory.Exists(Path.Combine(_root, ok.Id.ToString("N"))));
        Assert.True(Directory.Exists(Path.Combine(_root, remaining.Id.ToString("N"))));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condição não atingida a tempo.");
            await Task.Delay(10);
        }
    }
}
