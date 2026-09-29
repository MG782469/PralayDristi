namespace PralayDrishti.Domain.Enums;

public enum UserRole { Viewer = 0, Analyst = 1, Admin = 2 }

public enum RunStatus { Queued, Running, Completed, Failed, Cancelled }

public enum RunOrigin { User, LakeWatch, Atlas }

public enum LakeState { New, Estimating, Simulating, Alerted, Dismissed, Failed }

public enum AlertSeverity { Moderate, Severe, Extreme }

// CAP 1.2 <status> values we actually issue
public enum AlertStatus { Exercise, Test, Actual }

public enum DamCategory { Reservoir, NaturalLake, Glacial, Transboundary }

public enum DamType
{
    Earthfill,
    Rockfill,
    Concrete,
    LandslideDam,   // natural blockage: landslide / debris
    MoraineDam      // glacial lake (GLOF) - South Lhonak type
}

public enum FailureMode { Overtopping, Piping, Instantaneous }

public enum BreachMethod { Froehlich2008, MacDonaldLangridgeMonopolis, CostaSchuster, UserDefined }
