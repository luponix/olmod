﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GameMod;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OLModUnitTest.HelperClasses;

namespace OLModUnitTest
{
    [TestClass]
    public class DownloadLevelUnitTest
    {
        [TestMethod]
        public void TestBasicClientDownload()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            callbacks.Verify(c => c.AddMPLevel(@"C:\ProgramData\Revival\Overload\testlevel.zip"));
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()));
        }

        [TestMethod]
        public void TestBasicServerDownload()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            callbacks.SetupGet(c => c.IsServer).Returns(true);
            callbacks.Setup(c => c.DownloadCompleted(It.IsAny<int>()));

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()));
        }

        [TestCategory("EnableLevel")]
        [TestMethod]
        public void TestEnableLevel()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();

            // Create a simulated level already on disk but disabled and not in the game list
            var file = fileSystem.CreateFile(fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip_OCT_Hidden");
            file.Levels.Add("testlevel.mp", 0xAF5E61F2);

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The algorithm should not download the level
            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            // The algorithm should still add the level
            callbacks.Verify(c => c.AddMPLevel(It.IsAny<string>()));
            // We expect the file to have been renamed too
            Assert.AreEqual("testlevel.zip", file.Filename);
            // DownloadCompleted should be signalled (note this means the "download attempt" succeeded,
            // not necessarily that anything actually had to be downloaded)
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()));
        }

        [TestCategory("EnableLevel")]
        [TestMethod]
        public void TestEnableLevel_UpperCase()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();

            var file = fileSystem.CreateFile(fileSystem.GetLevelDirectories()[0] + "\\TESTLEVEL.ZIP_OCT_Hidden");
            file.Levels.Add("TESTLEVEL.MP", 0xAF5E61F2);

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            callbacks.Verify(c => c.AddMPLevel(It.IsAny<string>()));
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()));
            Assert.AreEqual("TESTLEVEL.ZIP", file.Filename);
        }

        [TestCategory("EnableLevel")]
        [TestMethod]
        public void TestEnableLevel_DifferentFileName()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();

            var file = fileSystem.CreateFile(fileSystem.GetLevelDirectories()[0] + "\\testlevel2.zip_OCT_Hidden");
            file.Levels.Add("testlevel.mp", 0xAF5E61F2);

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            callbacks.Verify(c => c.AddMPLevel(It.IsAny<string>()));
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()));
            Assert.AreEqual("testlevel2.zip", file.Filename);
        }

        [TestCategory("EnableLevel")]
        [TestMethod]
        public void TestEnableLevel_DifferentVersionDisabled()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();

            // Create a simulated level currently in the game list
            string originalFilePath = fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip";
            var currentFile = fileSystem.CreateFile(originalFilePath);
            currentFile.Levels.Add("testlevel.mp", 0x6DAF487E);
            gameList.AddMPLevel(originalFilePath);
            // And a simulated level on disk but not in the game list
            var newFile = fileSystem.CreateFile(fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip_OCT_Hidden");
            newFile.Levels.Add("testlevel.mp", 0xAF5E61F2);

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The algorithm should not download the level
            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            // The algorithm should remove the old level
            callbacks.Verify(c => c.RemoveMPLevel(0));
            // The algorithm should add the new level
            callbacks.Verify(c => c.AddMPLevel(originalFilePath));
            // Both files should have been renamed
            Assert.AreEqual("testlevel.zip_OCT_Hidden1", currentFile.Filename);
            Assert.AreEqual("testlevel.zip", newFile.Filename);
        }

        [TestCategory("EnableLevel")]
        [TestMethod]
        public void TestEnableLevel_MultipleDifferentVersions()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();

            string originalFilePath = fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip";
            // Level #1 (current)
            var currentFile = fileSystem.CreateFile(originalFilePath);
            currentFile.Levels.Add("testlevel.mp", 0x6DAF487E);
            gameList.AddMPLevel(originalFilePath);
            // Level #2 (disabled, no match)
            var otherFile = fileSystem.CreateFile(fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip_OCT_Hidden");
            otherFile.Levels.Add("testlevel.mp", 0x7EF7033A);
            // Level #3 (disabled, match)
            var newFile = fileSystem.CreateFile(fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip_OCT_Hidden2");
            newFile.Levels.Add("testlevel.mp", 0xAF5E61F2);

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The algorithm should not download the level
            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            // The algorithm should remove the old level
            callbacks.Verify(c => c.RemoveMPLevel(0));
            // The algorithm should add the new level
            callbacks.Verify(c => c.AddMPLevel(originalFilePath));
            // Both files should have been renamed; other level should not
            Assert.AreEqual("testlevel.zip_OCT_Hidden1", currentFile.Filename);
            Assert.AreEqual("testlevel.zip_OCT_Hidden", otherFile.Filename);
            Assert.AreEqual("testlevel.zip", newFile.Filename);
        }

        [TestCategory("EnableLevel")]
        [TestMethod]
        public void TestEnableLevel_DifferentVersionDisableFails_Abort()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();

            string originalFilePath = fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip";
            // Currently loaded level is not writeable
            var currentFile = fileSystem.CreateFile(originalFilePath);
            currentFile.Locked = true;
            currentFile.Levels.Add("testlevel.mp", 0x6DAF487E);
            gameList.AddMPLevel(originalFilePath);

            var newFile = fileSystem.CreateFile(fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip_OCT_Hidden");
            newFile.Levels.Add("testlevel.mp", 0xAF5E61F2);

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The algorithm should not download the level
            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            // The algorithm should not remove the old level
            callbacks.Verify(c => c.RemoveMPLevel(It.IsAny<int>()), Times.Never);
            // The algorithm should not add the new level
            callbacks.Verify(c => c.AddMPLevel(It.IsAny<string>()), Times.Never);
            // The download should be reported as failed
            callbacks.Verify(c => c.DownloadFailed());
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()), Times.Never);
        }

        [TestCategory("EnableLevel")]
        [TestMethod]
        public void TestEnableLevelFails_Download()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            string originalFilePath = fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip";
            // Currently loaded level
            var currentFile = fileSystem.CreateFile(originalFilePath);
            currentFile.Levels.Add("testlevel.mp", 0x6DAF487E);
            gameList.AddMPLevel(originalFilePath);
            // Disabled level (not writeable)
            var newFile = fileSystem.CreateFile(fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip_OCT_Hidden");
            newFile.Locked = true;
            newFile.Levels.Add("testlevel.mp", 0xAF5E61F2);

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The algorithm should download the level
            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()));
            // The algorithm should remove the old level
            callbacks.Verify(c => c.RemoveMPLevel(0));
            // The algorithm should add the new level
            callbacks.Verify(c => c.AddMPLevel(originalFilePath));
            callbacks.Verify(c => c.DownloadCompleted(0));
            // The old level should be renamed
            Assert.AreEqual("testlevel.zip_OCT_Hidden1", currentFile.Filename);
        }

        [TestCategory("EnableLevel")]
        [TestMethod]
        public void TestFindDisabledLevel_LevelNotPresent_Skip()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            // Create a zip archive that does not contain the level
            var file = fileSystem.CreateFile(fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip_OCT_Hidden");

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The algorithm should download the level
            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()));
            // The algorithm should add the new level
            callbacks.Verify(c => c.AddMPLevel(It.IsAny<string>()));
            // The existing file should not have been renamed
            Assert.AreEqual("testlevel.zip_OCT_Hidden", file.Filename);
        }

        [TestCategory("EnableLevel")]
        [TestMethod]
        public void TestFindDisabledLevel_LevelNotDisabled_Skip()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            // Existing file #1: does not contain the correct level, loaded
            string otherFilePath = fileSystem.GetLevelDirectories()[0] + "\\otherlevel.zip";
            var otherFile = fileSystem.CreateFile(otherFilePath);
            otherFile.Levels.Add("otherlevel.mp", 0xCA98CF04);
            gameList.AddMPLevel(otherFilePath);

            // Existing file #2: does not contain the correct level, not loaded
            var otherFile2 = fileSystem.CreateFile(fileSystem.GetLevelDirectories()[0] + "\\otherlevel2.zip");
            otherFile2.Levels.Add("otherlevel2.mp", 0x92E41697);

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The algorithm should download the level
            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()));
            // The algorithm should add the new level
            string expectedFilePath = fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip";
            callbacks.Verify(c => c.AddMPLevel(expectedFilePath));
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()));
            // The existing files should not have been renamed
            Assert.AreEqual("otherlevel.zip", otherFile.Filename);
            Assert.AreEqual("otherlevel2.zip", otherFile2.Filename);
        }

        [TestMethod]
        public void TestDownloadLevel_UrlNotFound_Abort()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            // This time the server does not have the level

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The algorithm should not try to download the level
            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            // The algorithm should not add a level
            callbacks.Verify(c => c.AddMPLevel(It.IsAny<string>()), Times.Never);
            // The download should be reported as failed
            callbacks.Verify(c => c.DownloadFailed());
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()), Times.Never);
        }

        [TestCategory("GetLocalDownloadPath")]
        [TestMethod]
        public void TestGetLocalDownloadPath_CreateDirectory()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            // Override DirectoryExists to report that the first directory does not exist
            var firstDirectoryPath = fileSystem.GetLevelDirectories()[0];
            callbacks.Setup(c => c.DirectoryExists(It.IsAny<string>())).Returns((string path) =>
            {
                if (path == firstDirectoryPath)
                {
                    return false;
                }
                return fileSystem.DirectoryExists(path);
            });

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The algorithm should try to create the directory
            callbacks.Verify(c => c.CreateDirectory(firstDirectoryPath));
            // The algorithm should download the level
            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()));
            // The download should not be reported as failed
            callbacks.Verify(c => c.DownloadFailed(), Times.Never);
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()));
        }

        [TestCategory("GetLocalDownloadPath")]
        [TestMethod]
        public void TestGetLocalDownloadPath_CannotWriteFirstDirectory_UseSecondDirectory()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            // Lock the first directory so it can't be written
            var firstDirectory = fileSystem.GetDirectory(fileSystem.GetLevelDirectories()[0]);
            firstDirectory.Locked = true;

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The algorithm should download the level
            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()));
            // The algorithm should add the level to the second directory
            string expectedFilePath = fileSystem.GetLevelDirectories()[1] + "\\testlevel.zip";
            callbacks.Verify(c => c.AddMPLevel(expectedFilePath));
            // There should be no files in the first directory
            Assert.AreEqual(0, firstDirectory.Files.Count);
            // The download should not be reported as failed
            callbacks.Verify(c => c.DownloadFailed(), Times.Never);
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()));
        }

        [TestCategory("GetLocalDownloadPath")]
        [TestMethod]
        public void TestGetLocalDownloadPath_FileExistsLevelPresent_AddLevel()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            // The level is already on disk, with the right CRC, but not loaded
            string filePath = fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip";
            var file = fileSystem.CreateFile(filePath);
            file.Levels.Add("testlevel.mp", 0xAF5E61F2);

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The algorithm should not download the level
            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            // The algorithm should add the existing level
            callbacks.Verify(c => c.AddMPLevel(filePath));
            // The download should not be reported as failed
            callbacks.Verify(c => c.DownloadFailed(), Times.Never);
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()));
        }

        [TestCategory("GetLocalDownloadPath")]
        [TestMethod]
        public void TestGetLocalDownloadPath_FileExistsDifferentVersionPresent_DisableExistingLevel()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            string originalFilePath = fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip";
            // The level is already on disk, but with the wrong CRC
            var currentFile = fileSystem.CreateFile(originalFilePath);
            currentFile.Levels.Add("testlevel.mp", 0x6DAF487E);
            gameList.AddMPLevel(originalFilePath);

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The algorithm should download the new level
            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()));
            // The algorithm should add the new level
            callbacks.Verify(c => c.AddMPLevel(originalFilePath));
            // The existing file should have been renamed
            Assert.AreEqual("testlevel.zip_OCT_Hidden", currentFile.Filename);
            // The existing level should have been unloaded
            callbacks.Verify(c => c.RemoveMPLevel(0));
            // The download should not be reported as failed
            callbacks.Verify(c => c.DownloadFailed(), Times.Never);
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()));
        }

        [TestCategory("GetLocalDownloadPath")]
        [TestMethod]
        public void TestGetLocalDownloadPath_FileExistsLevelNotPresent_DisableExistingLevel()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            string originalFilePath = fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip";
            // There is a file on disk with the expected name, but without the correct level in it
            var currentFile = fileSystem.CreateFile(originalFilePath);
            currentFile.Levels.Add("otherlevel.mp", 0xCA98CF04);
            gameList.AddMPLevel(originalFilePath);

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The algorithm should download the new level
            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()));
            // The algorithm should add the new level
            callbacks.Verify(c => c.AddMPLevel(originalFilePath));
            // The existing file should have been renamed
            Assert.AreEqual("testlevel.zip_OCT_Hidden", currentFile.Filename);
            // The existing level should have been unloaded
            callbacks.Verify(c => c.RemoveMPLevel(0));
            // The download should not be reported as failed
            callbacks.Verify(c => c.DownloadFailed(), Times.Never);
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()));
        }

        [TestCategory("GetLocalDownloadPath")]
        [TestMethod]
        public void TestGetLocalDownloadPath_FileExistsWriteProtected_Abort()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            string originalFilePath = fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip";
            // A different version of the level is on disk, but is write-protected
            var currentFile = fileSystem.CreateFile(originalFilePath);
            currentFile.Locked = true;
            currentFile.Levels.Add("testlevel.mp", 0x6DAF487E);
            gameList.AddMPLevel(originalFilePath);

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The algorithm should not download the new level
            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            // The new level should not be added
            callbacks.Verify(c => c.AddMPLevel(It.IsAny<string>()), Times.Never);
            // The existing level should not be unloaded
            callbacks.Verify(c => c.RemoveMPLevel(It.IsAny<int>()), Times.Never);
            // The download should be reported as failed
            callbacks.Verify(c => c.DownloadFailed());
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()), Times.Never);
        }

        [TestCategory("GetLocalDownloadPath")]
        [TestMethod]
        public void TestGetLocalDownloadPath_DifferentVersionPresentDifferentFile_DisableExistingLevel()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            // A different version of the level is already loaded from a different .zip
            string currentFilePath = fileSystem.GetLevelDirectories()[0] + "\\testlevel2.zip";
            var currentFile = fileSystem.CreateFile(currentFilePath);
            currentFile.Levels.Add("testlevel.mp", 0x6DAF487E);
            gameList.AddMPLevel(currentFilePath);

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The algorithm should download the new level
            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()));
            // The algorithm should add the new level
            string expectedFilePath = fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip";
            callbacks.Verify(c => c.AddMPLevel(expectedFilePath));
            // The existing file should have been renamed
            Assert.AreEqual("testlevel2.zip_OCT_Hidden", currentFile.Filename);
            // The existing level should have been unloaded
            callbacks.Verify(c => c.RemoveMPLevel(0));
            // The download should not be reported as failed
            callbacks.Verify(c => c.DownloadFailed(), Times.Never);
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()));
        }

        [TestCategory("GetLocalDownloadPath")]
        [TestMethod]
        public void TestGetLocalDownloadPath_DisableExistingLevelCollateralDamage()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            string originalFilePath = fileSystem.GetLevelDirectories()[0] + "\\testlevel.zip";
            // The level is already on disk, but with the wrong CRC.
            // There is also a second level loaded from the same .zip.
            var currentFile = fileSystem.CreateFile(originalFilePath);
            currentFile.Levels.Add("testlevel.mp", 0x6DAF487E);
            currentFile.Levels.Add("otherlevel.mp", 0xCA98CF04);
            gameList.AddMPLevel(originalFilePath);

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The algorithm should download the new level
            callbacks.Verify(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()));
            // The algorithm should add the new level
            callbacks.Verify(c => c.AddMPLevel(originalFilePath));
            // The existing file should have been renamed
            Assert.AreEqual("testlevel.zip_OCT_Hidden", currentFile.Filename);
            // The existing levels should BOTH have been unloaded
            callbacks.Verify(c => c.RemoveMPLevel(It.IsAny<int>()), Times.Exactly(2));
            // The download should not be reported as failed
            callbacks.Verify(c => c.DownloadFailed(), Times.Never);
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()));
        }

        [TestCategory("GetLocalDownloadPath")]
        [TestMethod]
        public void TestGetLocalDownloadPath_NoWriteablePath_Abort()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            // Lock all directories
            foreach (var directoryPath in fileSystem.GetLevelDirectories())
            {
                var directory = fileSystem.GetDirectory(directoryPath);
                directory.Locked = true;
            }

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The level should not be added
            callbacks.Verify(c => c.AddMPLevel(It.IsAny<string>()), Times.Never);
            // The download should be reported as failed
            callbacks.Verify(c => c.DownloadFailed());
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()), Times.Never);
        }

        [TestMethod]
        public void TestDownloadFails_Abort()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            // Override DownloadLevel, cause it to do nothing (emulating a download failure)
            callbacks.Setup(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>())).Returns(new List<string>());

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The level should not be in the game list (it may have tried to add it but this should fail)
            Assert.AreEqual(0, gameList.GetMPLevels().Count());
            // There should be no files in any directory
            foreach (var directoryPath in fileSystem.GetLevelDirectories())
            {
                var directory = fileSystem.GetDirectory(directoryPath);
                Assert.AreEqual(0, directory.Files.Count);
            }
            // The download should be reported as failed
            callbacks.Verify(c => c.DownloadFailed());
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()), Times.Never);
        }

        [TestMethod]
        public void TestDownloadBadMission_Abort()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            // Override DownloadLevel, cause it to download the wrong level
            callbacks.Setup(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string url, string filename) =>
                {
                    var file = fileSystem.CreateFile(filename);
                    file.Levels.Add("testlevel.mp", 0x6DAF487E);
                    return new List<string>();
                });

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The level should not be in the game list (it may have tried to add it but this should fail)
            Assert.AreEqual(-1, gameList.GetAddOnLevelIndex("TESTLEVEL.MP:AF5E61F2"));
            // The download should be reported as failed
            callbacks.Verify(c => c.DownloadFailed());
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()), Times.Never);
        }

        [TestMethod]
        public void TestServerDownloadFails_ReportError()
        {
            (var fileSystem, var gameList, var serverFiles, var callbacks, var algorithm) = CreateDefaultAlgorithm();
            serverFiles.Add(Guid.NewGuid(), new FakeFileSystem.FakeFile { Levels = { { "testlevel.mp", 0xAF5E61F2 } } });

            // Override DownloadLevel, cause it to do nothing (emulating a download failure)
            callbacks.Setup(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>())).Returns(new List<string>());

            var iterator = algorithm.DoGetLevel("TESTLEVEL.MP:AF5E61F2");
            while (iterator.MoveNext()) ;

            // The level should not be in the game list (it may have tried to add it but this should fail)
            Assert.AreEqual(-1, gameList.GetAddOnLevelIndex("TESTLEVEL.MP:AF5E61F2"));
            // The download should be reported as failed
            callbacks.Verify(c => c.DownloadFailed());
            // The download should NOT be reported as completed
            callbacks.Verify(c => c.DownloadCompleted(It.IsAny<int>()), Times.Never);
        }

        private (FakeFileSystem fileSystem, FakeGameList gameList,
            Dictionary<Guid, FakeFileSystem.FakeFile> serverFiles, Mock<DownloadLevelCallbacks> callbacks,
            MPDownloadLevelAlgorithm algorithm) CreateDefaultAlgorithm()
        {
            var fileSystem = new FakeFileSystem(@"C:\ProgramData\Revival\Overload", @"E:\steamapps\common\Overload\DLC");
            var gameList = new FakeGameList(fileSystem);
            var serverFiles = new Dictionary<Guid, FakeFileSystem.FakeFile>();
            var callbacks = new Mock<DownloadLevelCallbacks> { CallBase = true };
            var algorithm = new MPDownloadLevelAlgorithm(callbacks.Object);

            callbacks.Setup(c => c.GetLevelDownloadUrl(It.IsAny<string>()))
                .Returns<string>(levelIdHash =>
                {
                    var results = serverFiles.Where(
                        file => file.Value.Levels.Keys.Contains(levelIdHash.Split(':')[0], StringComparer.OrdinalIgnoreCase));
                    if (results.Count() == 0)
                    {
                        return new List<string>();
                    }
                    var guid = results.First().Key;
                    var zipName = levelIdHash.Split('.', ':')[0].ToLower();
                    return new List<string> { $"https://overloadmaps.com/files/{guid:N}/{zipName}.zip" };
                });
            callbacks.Setup(c => c.DownloadLevel(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string url, string filename) =>
                {
                    var match = Regex.Match(url, @"^https://overloadmaps\.com/files/([0-9a-fA-F]{32})/\w+\.zip$");
                    var guid = Guid.Parse(match.Groups[1].Value);
                    if (serverFiles.ContainsKey(guid))
                    {
                        var file = fileSystem.CreateFile(filename);
                        foreach (var item in serverFiles[guid].Levels)
                        {
                            file.Levels.Add(item.Key, item.Value);
                        }
                    }
                    return new List<string>();
                });
            callbacks.Setup(c => c.GetMPLevels()).Returns(gameList.GetMPLevels());
            callbacks.Setup(c => c.AddMPLevel(It.IsAny<string>()))
                .Callback<string>(filename => gameList.AddMPLevel(filename));
            callbacks.Setup(c => c.RemoveMPLevel(It.IsAny<int>()))
                .Callback<int>(index => gameList.RemoveMPLevel(index));
            callbacks.Setup(c => c.GetAddOnLevelIndex(It.IsAny<string>()))
                .Returns<string>(levelIdHash => gameList.GetAddOnLevelIndex(levelIdHash));
            callbacks.SetupGet(c => c.IsServer).Returns(false);
            callbacks.Setup(c => c.CanCreateFile(It.IsAny<string>()))
                .Returns((string filename) => fileSystem.CanCreateFile(filename));
            callbacks.Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns((string filename) => fileSystem.FileExists(filename));
            callbacks.Setup(c => c.MoveFile(It.IsAny<string>(), It.IsAny<string>()))
                .Callback((string filePath, string newFilePath) => fileSystem.MoveFile(filePath, newFilePath));
            callbacks.Setup(c => c.DeleteFile(It.IsAny<string>()))
                .Callback<string>(filePath => fileSystem.DeleteFile(filePath));
            callbacks.Setup(c => c.ZipContainsLevel(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string zipFilename, string levelIdHash) =>
                fileSystem.ZipContainsLevel(zipFilename, levelIdHash));
            callbacks.SetupGet(c => c.LevelDirectories).Returns(fileSystem.GetLevelDirectories());
            callbacks.Setup(c => c.DirectoryExists(It.IsAny<string>()))
                .Returns((string path) => fileSystem.DirectoryExists(path));
            callbacks.Setup(c => c.GetDirectoryFiles(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string path, string searchPattern) => fileSystem.GetDirectoryFiles(path, searchPattern));
            callbacks.Setup(c => c.CreateDirectory(It.IsAny<string>()))
                .Returns((string path) => fileSystem.CreateDirectory(path));

            return (fileSystem, gameList, serverFiles, callbacks, algorithm);
        }
    }


}
