namespace TPSteelSeriesGG.SteelSeriesAPI;

public record SonarAudioConfiguration(string? Id, string? Name);

public record SonarVolumeSettings(double Volume, bool MuteState);

public record RedirectionDevice(string? Name, string Id);