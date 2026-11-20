using Microsoft.Extensions.Options;
using PhotoManager.Core.Workflows;
using Temporalio.Client;
using Temporalio.Client.Schedules;
using Temporalio.Exceptions;

namespace PhotoManager.Worker.Scheduling;

/// <summary>
/// Registers the nightly re-index as a Temporal Schedule at startup (create, or update if it already exists).
/// A Schedule rather than a sleeping workflow loop is the Temporal-recommended way to run periodic work: it
/// survives deployments, supports jitter, and skips overlapping runs. Every replica runs this; the
/// create/update pair is idempotent.
/// </summary>
/// <param name="client">Temporal client.</param>
/// <param name="options">Worker options.</param>
/// <param name="logger">Logger.</param>
public sealed class NightlyReindexScheduler(ITemporalClient client, IOptions<WorkerOptions> options, ILogger<NightlyReindexScheduler> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.NightlyReindexEnabled)
        {
            logger.LogInformation("Nightly re-index schedule disabled by configuration");
            return;
        }

        var schedule = new Schedule(
            Action: ScheduleActionStartWorkflow.Create(
                WorkflowTypes.ReindexArchive,
                [new ReindexArchiveInput()],
                new WorkflowOptions(WorkflowIds.ReindexArchive, TaskQueues.Batch)),
            Spec: new ScheduleSpec
            {
                CronExpressions = [options.Value.NightlyReindexCron],

                // Jitter spreads the start so a fleet of schedules (or a retried deploy) doesn't fire at once.
                Jitter = TimeSpan.FromMinutes(30),
            })
        {
            Policy = new SchedulePolicy
            {
                Overlap = Temporalio.Api.Enums.V1.ScheduleOverlapPolicy.Skip,
                CatchupWindow = TimeSpan.FromHours(1),
            },
        };

        for (var attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                try
                {
                    await client.CreateScheduleAsync(WorkflowIds.NightlyReindexSchedule, schedule);
                    logger.LogInformation("Created schedule {ScheduleId}", WorkflowIds.NightlyReindexSchedule);
                }
                catch (ScheduleAlreadyRunningException)
                {
                    await client.GetScheduleHandle(WorkflowIds.NightlyReindexSchedule).UpdateAsync(_ => new ScheduleUpdate(schedule));
                    logger.LogInformation("Updated schedule {ScheduleId}", WorkflowIds.NightlyReindexSchedule);
                }

                return;
            }
            catch (RpcException ex) when (attempt < 10)
            {
                logger.LogWarning(ex, "Registering the nightly schedule failed (attempt {Attempt}); retrying", attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * attempt)), stoppingToken);
            }
        }
    }
}
