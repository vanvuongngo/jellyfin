#pragma warning disable CS1591

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Server.Implementations;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Data
{
    public class CleanDatabaseScheduledTask : ILibraryPostScanTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<CleanDatabaseScheduledTask> _logger;
        private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;

        public CleanDatabaseScheduledTask(
            ILibraryManager libraryManager,
            ILogger<CleanDatabaseScheduledTask> logger,
            IDbContextFactory<JellyfinDbContext> dbProvider)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _dbProvider = dbProvider;
        }

        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            CleanDeadItems(cancellationToken, progress);
            return Task.CompletedTask;
        }

        private void CleanDeadItems(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var itemIds = _libraryManager.GetItemIds(new InternalItemsQuery
            {
                HasDeadParentId = true
            });

            var numComplete = 0;
            var numItems = itemIds.Count + 1;

            _logger.LogDebug("Cleaning {0} items with dead parent links", numItems);

            foreach (var itemId in itemIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = _libraryManager.GetItemById(itemId);

                if (item is not null)
                {
                    _logger.LogInformation("Cleaning item {0} type: {1} path: {2}", item.Name, item.GetType().Name, item.Path ?? string.Empty);

                    _libraryManager.DeleteItem(item, new DeleteOptions
                    {
                        DeleteFileLocation = false
                    });
                }

                numComplete++;
                double percent = numComplete;
                percent /= numItems;
                progress.Report(percent * 100);
            }

            using var context = _dbProvider.CreateDbContext();
            using var transaction = context.Database.BeginTransaction();
            context.ItemValues.Where(e => e.BaseItemsMap!.Count == 0).ExecuteDelete();
            transaction.Commit();

            progress.Report(100);
        }
    }
}
