using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Safehouse.Core;

namespace Safehouse.Data
{
    /// <summary>
    /// Where the account is kept: <c>account.json</c> in the data folder. Uses the same atomic-write and backup
    /// rules as <see cref="ProfileRepository"/>, but account-level data is a single file and a damaged file is
    /// restored from its backup.
    /// </summary>
    public sealed class AccountRepository
    {
        private readonly string _accountPath;
        private readonly string _backupPath;

        public AccountRepository(string dataFolder)
        {
            DataFolder = dataFolder;
            _accountPath = Path.Combine(dataFolder, "account.json");
            _backupPath = _accountPath + ".bak";
        }

        public string DataFolder { get; }

        public bool Exists => File.Exists(_accountPath);

        public Account Load(ICollection<string> notices = null)
        {
            byte[] raw = null;
            try
            {
                if (File.Exists(_accountPath))
                {
                    raw = File.ReadAllBytes(_accountPath);
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                throw new StorageException($"Cannot read account.json. The file was left unchanged.", error);
            }

            if (raw == null)
            {
                return Recover(hasPrimary: false, notices);
            }

            try
            {
                return Decode(raw);
            }
            catch (DamagedFileException)
            {
                return Recover(hasPrimary: true, notices);
            }
            catch (ValidationException error)
            {
                throw new StorageException(
                    $"Cannot load account.json:\n{error.Message}\nThe file was left unchanged.", error);
            }
        }

        public void Save(Account account)
        {
            var path = _accountPath;
            var backupPath = _backupPath;
            try
            {
                if (File.Exists(path))
                {
                    var current = File.ReadAllBytes(path);
                    if (!TryDecode(current, out _, out var problem))
                    {
                        throw new StorageException(
                            $"The saved account.json cannot be read ({problem}) so it was not overwritten. " +
                            "The requested change was not applied.");
                    }

                    AtomicFile.Write(backupPath, current);
                }
                else if (File.Exists(backupPath))
                {
                    throw new StorageException(
                        "account.json is missing but its backup exists. Reopen the data folder to restore the backup first. " +
                        "The requested change was not applied.");
                }

                AtomicFile.Write(path, AccountSerializer.Serialize(account));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                throw new StorageException($"Could not save account.json: {error.Message}\nThe requested change was not applied.", error);
            }
        }

        public Account CreateDefault()
        {
            var account = Account.CreateNew();
            Save(account);
            return account;
        }

        private Account Recover(bool hasPrimary, ICollection<string> notices)
        {
            if (!File.Exists(_backupPath))
            {
                throw new StorageException(hasPrimary
                    ? "account.json is damaged and has no backup to restore. It was left unchanged."
                    : "account.json is missing and has no backup.");
            }

            byte[] backup;
            Account account;
            try
            {
                backup = File.ReadAllBytes(_backupPath);
                account = Decode(backup);
            }
            catch (Exception error) when (error is DamagedFileException || error is ValidationException
                                          || error is IOException || error is UnauthorizedAccessException)
            {
                throw new StorageException(
                    "account.json cannot be opened and its backup cannot be used either. Nothing was changed.", error);
            }

            try
            {
                if (hasPrimary)
                {
                    var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss");
                    var recoveryFolder = Path.Combine(DataFolder, "recovery");
                    Directory.CreateDirectory(recoveryFolder);
                    AtomicFile.Write(
                        Path.Combine(recoveryFolder, $"account-{stamp}-{Guid.NewGuid().ToString("N").Substring(0, 6)}.json"),
                        File.ReadAllBytes(_accountPath));
                }

                AtomicFile.Write(_accountPath, backup);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                throw new StorageException("Could not restore account.json from its backup. Nothing was changed.", error);
            }

            notices?.Add("Recovered the account from the backup of the previous save.");
            return account;
        }

        private Account Decode(byte[] raw)
        {
            string text;
            try
            {
                text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(raw);
            }
            catch (DecoderFallbackException)
            {
                throw new DamagedFileException();
            }

            if (text.Length > 0 && text[0] == '﻿')
            {
                text = text.Substring(1);
            }

            JToken document;
            try
            {
                document = ProfileSerializer.Parse(text); // shared strict JSON reader
            }
            catch (JsonException)
            {
                throw new DamagedFileException();
            }

            return AccountSerializer.FromJson(document);
        }

        private bool TryDecode(byte[] raw, out Account account, out string problem)
        {
            try
            {
                account = Decode(raw);
                problem = null;
                return true;
            }
            catch (DamagedFileException)
            {
                problem = "The file is not a readable save (damaged JSON or text).";
            }
            catch (ValidationException error)
            {
                problem = error.Message;
            }

            account = null;
            return false;
        }

        private sealed class DamagedFileException : Exception { }
    }
}
