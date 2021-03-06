﻿/*
Copyright 2012, 2013, 2017 Adam Carter (http://adam-carter.com)

This file is part of FileCache (http://github.com/acarteas/FileCache).

FileCache is distributed under the Apache License 2.0.
Consult "LICENSE.txt" included in this package for the Apache License 2.0.
*/
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Caching;
using System.Threading;

namespace FC.UnitTests
{
    /// <summary>
    ///This is a test class for FileCacheTest and is intended
    ///to contain all FileCacheTest Unit Tests
    ///</summary>
    [TestClass]
    public class FileCacheTest
    {
        FileCache _cache;

        [TestCleanup]
        public void Cleanup()
        {
            _cache?.Clear(); // Clears the cache after every Test
        }

        [TestMethod]
        public void AbsoluteExpirationTest()
        {
            _cache = new FileCache();
            CacheItemPolicy policy = new CacheItemPolicy();

            //add an item and have it expire yesterday
            policy.AbsoluteExpiration = (DateTimeOffset)DateTime.Now.AddDays(-1);
            _cache.Set("test", "test", policy);

            //then try to access the item
            object result = _cache.Get("test");
            result.Should().BeNull();
        }

        [TestMethod]
        public void PolicySaveTest()
        {
            _cache = new FileCache();
            CacheItemPolicy policy = new CacheItemPolicy();
            policy.SlidingExpiration = new TimeSpan(1, 0, 0, 0, 0);
            _cache.Set("test", "test", policy);

            CacheItemPolicy returnPolicy = _cache.GetPolicy("test");
            policy.SlidingExpiration.Should().Be(returnPolicy.SlidingExpiration);
        }

        [TestMethod]
        public void SlidingExpirationTest()
        {
            _cache = new FileCache();
            CacheItemPolicy policy = new CacheItemPolicy();

            //add an item and have it expire 500 ms from now
            policy.SlidingExpiration = new TimeSpan(0, 0, 0, 0, 500);
            _cache.Set("test", "test", policy);

            //sleep for 200
            Thread.Sleep(200);

            //then try to access the item
            object result = _cache.Get("test");
            result.Should().Be("test");

            //sleep for another 200
            Thread.Sleep(200);

            //then try to access the item
            result = _cache.Get("test");
            result.Should().Be("test");

            //then sleep for more than 500 ms.  Should be gone
            Thread.Sleep(600);
            result = _cache.Get("test");
            result.Should().BeNull();
        }

        [TestMethod]
        public void CustomObjectSaveTest()
        {
            _cache = new FileCache();

            //create custom object
            CustomObjB customBefore = new CustomObjB()
            {
                Num = 5,
                obj = new CustomObjA()
                {
                    Name = "test"
                }
            };

            CacheItem item = new CacheItem("foo")
            {
                Value = customBefore,
                RegionName = "foobar"
            };

            //set it
            _cache.Set(item, new CacheItemPolicy());

            //now get it back
            CacheItem fromCache = _cache.GetCacheItem("foo", "foobar");

            //pulling twice increases code coverage
            fromCache = _cache.GetCacheItem("foo", "foobar");

            var customAfter = fromCache.Value as CustomObjB;

            customAfter.Should().BeEquivalentTo(customBefore);
        }

        [TestMethod]
        public void CacheSizeTest()
        {
            _cache = new FileCache("CacheSizeTest");

            _cache["foo"] = "bar";
            _cache["foo"] = "foobar";

            long cacheSize = _cache.GetCacheSize();
            cacheSize.Should().NotBe(0);

            _cache.Remove("foo");
            cacheSize = _cache.GetCacheSize();
            cacheSize.Should().Be(0);
            cacheSize = _cache.CurrentCacheSize;
            cacheSize.Should().Be(0);
        }

        [TestMethod]
        public void MaxCacheSizeTest()
        {
            _cache = new FileCache("MaxCacheSizeTest");
            _cache.MaxCacheSize = 0;
            bool isEventCalled = false;
            _cache.MaxCacheSizeReached += delegate(object sender, FileCacheEventArgs e)
            {
                isEventCalled = true;
            };
            _cache["foo"] = "bar";

            isEventCalled.Should().BeTrue();
        }

        [TestMethod]
        public void ShrinkCacheTest()
        {
            _cache = new FileCache("ShrinkTest");
            Thread.Sleep(500); // Added because appveyor would get -1L in the assertion below. So it wasn't able to acquire the lock

            // Test empty case
            _cache.ShrinkCacheToSize(0).Should().Be(0);

            // Insert 4 items, and keep track of their size
            //sleep to make sure that oldest item gets removed first
            _cache["item1"] = "bar1asdfasdfdfskjslkjlkjsdf sdlfkjasdlf asdlfkjskjfkjs d sdkfjksjd";
            Thread.Sleep(500);
            long size1 = _cache.GetCacheSize();
            _cache["item2"] = "bar2sdfjkjk skdfj sdflkj sdlkj lkjkjkjkjkjssss";
            Thread.Sleep(500);
            long size2 = _cache.GetCacheSize() - size1;
            _cache["item3"] = "bar3ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
            Thread.Sleep(500);
            long size3 = _cache.GetCacheSize() - size2 - size1; 
            _cache["item4"] = "bar3fffffffffsdfsdfffffffffffffsdfffffffffffdddddddddddddddddddffffff";
            long size4 = _cache.GetCacheSize() - size3 - size2 - size1;

            // Shrink to the size of the last 3 items (should remove item1 because it's the oldest, keeping the other 3)
            long newSize = _cache.ShrinkCacheToSize(size4 + size3 + size2);
            newSize.Should().Be(size4 + size3 + size2);

            // Shrink to just smaller than two items (should keep just item4, delete item2 and item3)
            newSize = _cache.ShrinkCacheToSize(size3 + size4 - 1);
            newSize.Should().Be(size4);

            // Shrink to size 1 (should delete everything)
            newSize = _cache.ShrinkCacheToSize(1);
            newSize.Should().Be(0);
        }

        [TestMethod]
        public void AutoShrinkTest()
        {
            _cache = new FileCache("AutoShrinkTest");

            _cache.Flush();
            _cache.MaxCacheSize = 20000;
            _cache.CacheResized += delegate(object sender, FileCacheEventArgs args)
            {
                _cache["foo0"].Should().BeNull();
                _cache["foo10"].Should().NotBeNull();
                _cache["foo40"].Should().NotBeNull();
            };


            for (int i = 0; i < 100; i++)
            {
                _cache["foo" + i] = "bar";

                // test to make sure it doesn't crash if one of the files is missing
                if (i == 10)
                    File.Delete(_cache.CacheDir + "/cache/foo9.dat");

                // test to make sure it leaves items that have been recently accessed.
                if (i%5 == 0 && i != 0)
                {
                    var foo10 = _cache.Get("foo10");
                    var foo40 = _cache.Get("foo40");
                }
            }
        }

        [TestMethod]
        public void FlushTest()
        {
            _cache = new FileCache("FlushTest");

            _cache["foo"] = "bar";
            Thread.Sleep(500);

            //attempt flush
            _cache.Flush(DateTime.Now.AddDays(1));

            Thread.Sleep(500);

            //check to see if size ends up at zero (expected result)
            _cache.GetCacheSize().Should().Be(0);
        }

        [TestMethod]
        public void RemoveTest()
        {
            _cache = new FileCache();
            _cache.Set("test", "test", DateTimeOffset.Now.AddDays(3));
            object result = _cache.Get("test");
            result.Should().Be("test");

            //now delete
            _cache.Remove("test");
            result = _cache["test"];
            result.Should().BeNull();

            //check file system to be sure item was removed
            string cachePath = Path.Combine(_cache.CacheDir, "cache", "test.dat");
            File.Exists(cachePath).Should().BeFalse();

            //check file system to be sure that policy file was removed
            string policyPath = Path.Combine(_cache.CacheDir, "policy", "test.policy");
            File.Exists(policyPath).Should().BeFalse();
        }

        [TestMethod]
        public void TestCount()
        {
            _cache = new FileCache("testCount");

            _cache["test"] = "test";

            object result = _cache.Get("test");
            
            result.Should().Be("test");
            _cache.GetCount().Should().Be(1);
        }

        [TestMethod]
        public void DefaultRegionTest()
        {
            FileCache cacheWithDefaultRegion = new FileCache();
            cacheWithDefaultRegion.DefaultRegion = "foo";
            FileCache defaultCache = new FileCache();
            cacheWithDefaultRegion["foo"] = "bar";
            object pull = defaultCache.Get("foo", "foo");
            pull.ToString().Should().Be("bar");

            cacheWithDefaultRegion.Flush();
            defaultCache.Flush();
        }

        [TestMethod]
        public void ClearCache()
        {
            _cache = new FileCache();
            _cache["MyFirstKey"] = "MyFirstValue";
            object pull = _cache.Get("MyFirstKey");
            pull.ToString().Should().Be("MyFirstValue");
        }

        /// <summary>
        /// Generated to debug and address github issue #2
        /// </summary>
        [TestMethod]
        public void CustomRegionTest()
        {
            _cache = new FileCache();
            var policy = new CacheItemPolicy { SlidingExpiration = new TimeSpan(0, 5, 0) };

            var key = "my_key";
            var region = "my_region";
            object value = null;

            _cache.Add(key, "foo", policy, region);
            _cache.Add(key, "bar", policy);

            value = _cache.Get(key); // returns: "bar"
            value.Should().Be("bar");
            value = _cache.Get(key, region); // returns: "foo"
            value.Should().Be("foo");
            value = _cache.Get(key); // returns: "foo" ?!
            value.Should().Be("bar");
        }

        [TestMethod]
        public void AccessTimeoutTest()
        {
            //AC: This test passes in debug mode, but not in rutime mode.  Why?

            _cache = new FileCache();
            _cache.AccessTimeout = new TimeSpan(1);
            _cache["primer"] = 0;
            string filePath = Path.Combine(_cache.CacheDir, "cache", "foo.dat");
            FileStream stream = File.Open(filePath, FileMode.Create);
            try
            {
                object result = _cache["foo"];

                //file access should fail.  If it doesn't, the test fails.
                true.Should().BeFalse();
            }
            catch (IOException)
            {
                //we expect a file exception.
                true.Should().BeTrue();
            }
            stream.Close();
        }

        [TestMethod]
        public void DefaultPolicyTest()
        {
            _cache = new FileCache();
            CacheItemPolicy policy = new CacheItemPolicy();
            policy.SlidingExpiration = new TimeSpan(1);
            _cache.DefaultPolicy = policy;
            _cache["foo"] = "bar";
            Thread.Sleep(2);
            object result = _cache["foo"];
            result.Should().BeNull();
        }

        [TestMethod]
        public void GetEnumeratorTest()
        {
            _cache = new FileCache();
            _cache["foo"] = 1;
            _cache["bar"] = 2;

            foreach (KeyValuePair<string, object> kvp in _cache)
            {
                _cache[kvp.Key].Should().Be(kvp.Value);
            }
        }

        [TestMethod]
        public void CleanCacheTest()
        {
            _cache = new FileCache("CleanCacheTest");

            _cache.Add("foo", 1, DateTime.Now); // expires immediately
            _cache.Add("bar", 2, DateTime.Now + TimeSpan.FromDays(1)); // set to expire tomorrow

            _cache.CleanCache();

            _cache["foo"].Should().BeNull();
            _cache["bar"].Should().NotBeNull();
        }

        [TestMethod]
        public void RawFileCacheTest()
        {
            // Test with filename based payload
            FileCache perfCache = new FileCache("filePayload");
            perfCache.PayloadReadMode = FileCache.PayloadMode.Filename;
            perfCache.PayloadWriteMode = FileCache.PayloadMode.RawBytes;

            perfCache["mybytes"] = new byte[] { 4, 2 };
            perfCache.Get("mybytes").Should().BeOfType(typeof(string));
            File.Exists((string)perfCache.Get("mybytes")).Should().BeTrue();
        }


        [TestMethod]
        public void RawFileWithExpiryTest()
        {
            // Test with sliding expiry
            FileCache perfCacheWithExpiry = new FileCache("filePayloadExpiry");
            var pol = new CacheItemPolicy();
            pol.SlidingExpiration = new TimeSpan(0, 0, 2);
            perfCacheWithExpiry.DefaultPolicy = pol;
            perfCacheWithExpiry.FilenameAsPayloadSafetyMargin = new TimeSpan(0, 0, 1);
            perfCacheWithExpiry.PayloadReadMode = FileCache.PayloadMode.Filename;
            perfCacheWithExpiry.PayloadWriteMode = FileCache.PayloadMode.RawBytes;

            perfCacheWithExpiry["mybytes"] = new byte[] { 4, 2 };
            File.Exists((string)perfCacheWithExpiry.Get("mybytes")).Should().BeTrue();
            Thread.Sleep(2000);
            perfCacheWithExpiry.Get("mybytes").Should().BeNull();
        }
    }
}
