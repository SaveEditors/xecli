using System;
using System.IO;
using System.Linq;
using NoDev.Xdbf;
using NoDev.Xdbf.Records;

using XdbfNamespace = NoDev.Xdbf.Namespace;

namespace Xbox360.Remote.Cli.LocalProfiles;

internal sealed partial class ProfilePackage
{
	private const ulong GamercardCreditEarnedSettingId = 0x10040006;
	private const ulong GamercardAchievementsEarnedSettingId = 0x10040013;

	public bool HasSetting(ulong settingId)
	{
		if (!HasDashboardData)
		{
			return false;
		}

		DataFile? dashboardData = null;
		try
		{
			dashboardData = OpenDataFile($"{DashboardTitleId:X8}.gpd", DataFileOrigin.Profile);
			return dashboardData.RecordExists(XdbfNamespace.Settings, settingId);
		}
		finally
		{
			dashboardData?.Close();
		}
	}

	public void SetGamertag(string gamertag)
	{
		if (!HasAccount)
		{
			throw new InvalidOperationException("Profile package does not contain an Account file.");
		}

		ProfileAccountInfo account = ReadAccount();
		account.SetGamertag(gamertag);
		StagePackageFile("Account", account.ToArray());
		CommitPackageFiles("Account");
	}

	public ProfileSettingInfo WriteSetting(ulong settingId, ProfileSettingInfo setting)
	{
		if (!HasDashboardData)
		{
			throw new InvalidOperationException("Profile package does not contain FFFE07D1.gpd.");
		}

		string dashboardFile = $"{DashboardTitleId:X8}.gpd";
		DataFile? dashboardData = null;
		try
		{
			dashboardData = OpenDataFile(dashboardFile, DataFileOrigin.Profile);
			dashboardData.UpdateOrInsertRecord(XdbfNamespace.Settings, settingId, setting.ToArray());
		}
		finally
		{
			dashboardData?.Close();
		}

		CommitPackageFiles(dashboardFile);
		return ReadSetting(settingId);
	}

	public ProfileTitleInfo WriteTitle(ProfileTitleInfo title, bool replaceExisting)
	{
		if (!HasDashboardData)
		{
			throw new InvalidOperationException("Profile package does not contain FFFE07D1.gpd.");
		}

		string dashboardFile = $"{DashboardTitleId:X8}.gpd";
		DataFile? dashboardData = null;
		try
		{
			dashboardData = OpenDataFile(dashboardFile, DataFileOrigin.Profile);
			bool exists = dashboardData.RecordExists(XdbfNamespace.Titles, title.TitleId);
			if (exists && !replaceExisting)
			{
				throw new InvalidOperationException("Title record already exists: 0x" + title.TitleId.ToString("X8") + ".");
			}

			dashboardData.UpdateOrInsertRecord(XdbfNamespace.Titles, title.TitleId, title.ToArray());
			SyncDashboardSummarySettings(dashboardData);
		}
		finally
		{
			dashboardData?.Close();
		}

		CommitPackageFiles(dashboardFile);
		return ReadTitles().Single(candidate => candidate.TitleId == title.TitleId);
	}

	public ProfileAchievementInfo UnlockAchievement(uint titleId, uint achievementId, bool online, DateTime? achievedAtUtc)
	{
		return SetAchievementState(titleId, achievementId, unlock: true, online, achievedAtUtc);
	}

	public ProfileAchievementInfo LockAchievement(uint titleId, uint achievementId)
	{
		return SetAchievementState(titleId, achievementId, unlock: false, online: false, achievedAtUtc: null);
	}

	private ProfileAchievementInfo SetAchievementState(uint titleId, uint achievementId, bool unlock, bool online, DateTime? achievedAtUtc)
	{
		if (!HasDashboardData)
		{
			throw new InvalidOperationException("Profile package does not contain FFFE07D1.gpd.");
		}

		if (!HasTitleDataFile(titleId))
		{
			throw new FileNotFoundException("Title GPD not found in the profile package for " + titleId.ToString("X8") + ".");
		}

		string dashboardFile = $"{DashboardTitleId:X8}.gpd";
		string titleFile = $"{titleId:X8}.gpd";

		DataFile? dashboardData = null;
		DataFile? titleData = null;
		try
		{
			dashboardData = OpenDataFile(dashboardFile, DataFileOrigin.Profile);
			titleData = OpenDataFile(titleFile, DataFileOrigin.Profile);

			DataFileRecord? achievementRecord = titleData.GetRecord(XdbfNamespace.Achievements, achievementId);
			if (achievementRecord == null)
			{
				throw new InvalidOperationException("Achievement record not found: 0x" + achievementId.ToString("X8") + ".");
			}

			DataFileRecord? titleRecordHandle = dashboardData.GetRecord(XdbfNamespace.Titles, titleId);
			if (titleRecordHandle == null)
			{
				throw new InvalidOperationException("Title record not found in dashboard data: 0x" + titleId.ToString("X8") + ".");
			}

			ProfileAchievementInfo achievement = ProfileAchievementInfo.Parse(titleData.GetData(achievementRecord));
			ProfileTitleInfo title = ProfileTitleInfo.Parse(dashboardData.GetData(titleRecordHandle));

			int achievementDelta;
			int creditDelta;
			bool removeAchievementSync = false;
			bool removeAchievementImage = false;
			if (unlock)
			{
				if (online)
				{
					achievement.UnlockOnline(achievedAtUtc ?? DateTime.UtcNow);
				}
				else
				{
					achievement.UnlockOffline();
				}

				achievementDelta = 1;
				creditDelta = achievement.Credit;
			}
			else
			{
				achievement.Lock();
				removeAchievementSync = true;
				removeAchievementImage = titleData.RecordExists(XdbfNamespace.Images, achievement.ImageId);
				achievementDelta = -1;
				creditDelta = -achievement.Credit;
			}

			titleData.UpdateData(XdbfNamespace.Achievements, achievement.Id, achievement.ToArray());
			if (removeAchievementSync)
			{
				titleData.RemoveSyncRecord(XdbfNamespace.Achievements, achievement.Id);
			}

			if (removeAchievementImage)
			{
				titleData.RemoveRecord(XdbfNamespace.Images, achievement.ImageId);
			}

			title.ApplyAchievementDelta(achievementDelta, creditDelta);
			dashboardData.UpdateData(XdbfNamespace.Titles, titleId, title.ToArray());

			IncrementOrCreateInt32Setting(dashboardData, GamercardAchievementsEarnedSettingId, achievementDelta);
			IncrementOrCreateInt32Setting(dashboardData, GamercardCreditEarnedSettingId, creditDelta);
		}
		finally
		{
			titleData?.Close();
			dashboardData?.Close();
		}

		CommitPackageFiles(titleFile, dashboardFile);
		return ReadAchievements(titleId).Single(achievement => achievement.Id == achievementId);
	}

	private static void IncrementOrCreateInt32Setting(DataFile dataFile, ulong settingId, int delta)
	{
		if (!dataFile.RecordExists(XdbfNamespace.Settings, settingId))
		{
			SetOrCreateInt32Setting(dataFile, settingId, delta);
			return;
		}

		ProfileSettingInfo existing = ProfileSettingInfo.Parse(dataFile.GetData(XdbfNamespace.Settings, settingId));
		if (existing.Type != ProfileSettingType.Int32 && existing.Type != ProfileSettingType.Context)
		{
			throw new InvalidOperationException(
				"Cannot update non-Int32 profile summary setting " + settingId.ToString("X8") + ".");
		}

		int currentValue = Convert.ToInt32(existing.Value ?? 0, System.Globalization.CultureInfo.InvariantCulture);
		SetOrCreateInt32Setting(dataFile, settingId, currentValue + delta);
	}

	private static void SyncDashboardSummarySettings(DataFile dashboardData)
	{
		var titles = dashboardData
			.GetRecords(XdbfNamespace.Titles)
			.Select(record => ProfileTitleInfo.Parse(dashboardData.GetData(record)))
			.ToList();

		SetOrCreateInt32Setting(dashboardData, GamercardAchievementsEarnedSettingId, titles.Sum(title => title.AchievementsEarned));
		SetOrCreateInt32Setting(dashboardData, GamercardCreditEarnedSettingId, titles.Sum(title => title.CreditEarned));
	}

	private static void SetOrCreateInt32Setting(DataFile dataFile, ulong settingId, int value)
	{
		if (!dataFile.RecordExists(XdbfNamespace.Settings, settingId))
		{
			dataFile.UpdateOrInsertRecord(
				XdbfNamespace.Settings,
				settingId,
				ProfileSettingInfo.Create(settingId, ProfileSettingType.Int32, value).ToArray());
			return;
		}

		ProfileSettingInfo existing = ProfileSettingInfo.Parse(dataFile.GetData(XdbfNamespace.Settings, settingId));
		if (existing.Type != ProfileSettingType.Int32 && existing.Type != ProfileSettingType.Context)
		{
			throw new InvalidOperationException(
				"Cannot update non-Int32 profile summary setting " + settingId.ToString("X8") + ".");
		}

		dataFile.UpdateData(
			XdbfNamespace.Settings,
			settingId,
			existing.WithValue(value).ToArray());
	}

	private void StagePackageFile(string fileName, byte[] data)
	{
		string tempPath = EnsureTempFile(fileName);
		File.WriteAllBytes(tempPath, data);
	}

	private void CommitPackageFiles(params string[] fileNames)
	{
		foreach (string fileName in fileNames.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			string tempPath = EnsureTempFile(fileName);
			byte[] data = File.ReadAllBytes(tempPath);
			_reader.WriteFile(fileName, data);
		}

		_reader.SavePackageHeader();
	}
}
