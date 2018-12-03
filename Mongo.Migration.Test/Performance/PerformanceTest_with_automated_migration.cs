﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using FluentAssertions;
using Mongo.Migration.Services.Initializers;
using Mongo.Migration.Test.TestDoubles;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace Mongo.Migration.Test.Performance
{
    [TestFixture]
    public class PerformanceTest_with_automated_migration
    {
        [TearDown]
        public void TearDown()
        {
            MongoMigration.Reset();
            _client = null;
            _runner.Dispose();
        }

        [SetUp]
        public void SetUp()
        {
            _runner = MongoDbRunner.Start();
            _client = new MongoClient(_runner.ConnectionString);
        }

        #region PRIVATE

        private const int DOCUMENT_COUNT = 2500;

        private const string DATABASE_NAME = "PerformanceTest";

        private const string COLLECTION_NAME = "Test";

        private const int TOLERANCE_MS = 950;

        private MongoClient _client;
        private MongoDbRunner _runner;

        private void InsertMany(int number, bool withVersion)
        {
            var documents = new List<BsonDocument>();
            for (var n = 0; n < number; n++)
            {
                var document = new BsonDocument
                {
                    {"Dors", 3}
                };
                if (withVersion)
                    document.Add("Version", "0.0.0");
                documents.Add(document);
            }

            _client.GetDatabase(DATABASE_NAME).GetCollection<BsonDocument>(COLLECTION_NAME).InsertManyAsync(documents)
                .Wait();
        }

        private void MigrateAll(bool withVersion)
        {
            if (withVersion)
            {
                var versionedCollectin = _client.GetDatabase(DATABASE_NAME)
                    .GetCollection<TestDocumentWithAutoMigrate>(COLLECTION_NAME);
                var versionedResult = versionedCollectin.FindAsync(_ => true).Result.ToListAsync().Result;
                return;
            }

            var collection = _client.GetDatabase(DATABASE_NAME)
                .GetCollection<TestClass>(COLLECTION_NAME);
            var result = collection.FindAsync(_ => true).Result.ToListAsync().Result;
        }

        private void AddDocumentsToCache()
        {
            InsertMany(DOCUMENT_COUNT, false);
            MigrateAll(false);
        }

        private void ClearCollection()
        {
            _client.GetDatabase(DATABASE_NAME).DropCollection(COLLECTION_NAME);
        }

        #endregion

        [Test]
        public void When_migrating_number_of_documents_with_automated_save()
        {
            // Arrange
            // Worm up MongoCache
            ClearCollection();
            AddDocumentsToCache();
            ClearCollection();

            // Act
            // Measure time of MongoDb processing without Mongo.Migration
            var sw = new Stopwatch();
            sw.Start();
            InsertMany(DOCUMENT_COUNT, false);
            MigrateAll(false);
            sw.Stop();

            ClearCollection();

            // Measure time of MongoDb processing without Mongo.Migration
            MongoMigration.Initialize(_client);

            var swWithMigration = new Stopwatch();
            swWithMigration.Start();
            InsertMany(DOCUMENT_COUNT, true);
            MigrateAll(true);
            swWithMigration.Stop();

            var result = swWithMigration.ElapsedMilliseconds - sw.ElapsedMilliseconds;

            Console.WriteLine(
              $"First run with automation: MongoDB: {sw.ElapsedMilliseconds}ms, Mongo.Migration: {swWithMigration.ElapsedMilliseconds}ms, Diff: {result}ms (Tolerance: {TOLERANCE_MS}ms), Documents: {DOCUMENT_COUNT}, Migrations per Document: 2");
            
            var swSecondWithMigration = new Stopwatch();
            swSecondWithMigration.Start();
            MigrateAll(true);
            swSecondWithMigration.Stop();
            
            result = swSecondWithMigration.ElapsedMilliseconds - sw.ElapsedMilliseconds;
            
            Console.WriteLine(
                $"Second run with automation: Mongo.Migration: {swSecondWithMigration.ElapsedMilliseconds}ms, Documents: {DOCUMENT_COUNT}, Migrations per Document: 2"); 
            
            ClearCollection();

            // Assert
            result.Should().BeLessThan(TOLERANCE_MS);
        }
    }
}