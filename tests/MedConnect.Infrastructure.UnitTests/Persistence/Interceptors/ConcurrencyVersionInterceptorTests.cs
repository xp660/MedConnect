using FluentAssertions;
using MedConnect.Infrastructure.Persistence.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MedConnect.Infrastructure.UnitTests.Persistence.Interceptors;

/// <summary>
/// ConcurrencyVersionInterceptor 是 EF Core provider-agnostic 的一般 SaveChangesInterceptor，
/// 邏輯只碰 ChangeTracker，不碰任何 MySQL 專屬語法，所以這裡刻意不用 MedConnectDbContext
/// （會牽扯到 tinyint/generated column 等 MySQL 專屬設定），改用一個最小的測試專用 DbContext。
/// 用的是 SQLite in-memory（真的關聯式資料庫，會執行真正的 SQL），不是 CLAUDE.md §8.4 禁止的
/// EF Core InMemory Provider（那個連 WHERE version = ? 這種 UPDATE 語意都不會模擬）。
/// </summary>
public class ConcurrencyVersionInterceptorTests
{
    private class VersionedEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Version { get; set; }
    }

    private class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<VersionedEntity> Entities => Set<VersionedEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<VersionedEntity>().Property(e => e.Version).IsConcurrencyToken();
        }
    }

    private static TestDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new ConcurrencyVersionInterceptor())
            .Options;

        return new TestDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntityIsModified_IncrementsConcurrencyTokenByExactlyOne()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using (var setupContext = CreateContext(connection))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Entities.Add(new VersionedEntity { Name = "original" });
            await setupContext.SaveChangesAsync();
        }

        using var context = CreateContext(connection);
        var entity = await context.Entities.SingleAsync();
        var originalVersion = entity.Version;

        entity.Name = "changed";
        await context.SaveChangesAsync();

        entity.Version.Should().Be(originalVersion + 1);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntityIsModified_PersistsIncrementedVersionToDatabase()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using (var setupContext = CreateContext(connection))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Entities.Add(new VersionedEntity { Name = "original" });
            await setupContext.SaveChangesAsync();
        }

        using (var context = CreateContext(connection))
        {
            var entity = await context.Entities.SingleAsync();
            entity.Name = "changed";
            await context.SaveChangesAsync();
        }

        using var verifyContext = CreateContext(connection);
        var reloaded = await verifyContext.Entities.SingleAsync();
        reloaded.Version.Should().Be(1);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntityIsNewlyAdded_DoesNotTouchVersion()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();

        var entity = new VersionedEntity { Name = "brand new" };
        context.Entities.Add(entity);
        await context.SaveChangesAsync();

        entity.Version.Should().Be(0);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTwoContextsEditTheSameRow_SecondSaveThrowsConcurrencyException()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using (var setupContext = CreateContext(connection))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Entities.Add(new VersionedEntity { Name = "original" });
            await setupContext.SaveChangesAsync();
        }

        using var contextA = CreateContext(connection);
        using var contextB = CreateContext(connection);

        var entityA = await contextA.Entities.SingleAsync();
        var entityB = await contextB.Entities.SingleAsync();

        entityA.Name = "changed by A";
        await contextA.SaveChangesAsync();

        entityB.Name = "changed by B";
        var act = async () => await contextB.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
