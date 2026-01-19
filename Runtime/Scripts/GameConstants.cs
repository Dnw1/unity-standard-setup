/// <summary>
/// Centralized constants for scene names, video names, and audio names.
/// Replaces magic strings throughout the codebase for compile-time safety and easier maintenance.
/// </summary>
public static class GameConstants
{
    /// <summary>
    /// Scene name constants used in SceneFlag comparisons and scene loading.
    /// </summary>
    public static class Scenes
    {
        /// <summary>
        /// Word Shooter scene flag name: "1-Word"
        /// </summary>
        public const string WordShooter = "1-Word";
        
        /// <summary>
        /// Quiz scene flag name: "2-Quiz"
        /// </summary>
        public const string Quiz = "2-Quiz";
        
        /// <summary>
        /// Beat Saber scene flag name: "3-BeatSaber"
        /// </summary>
        public const string BeatSaber = "3-BeatSaber";
        
        /// <summary>
        /// Diploma/Future scene flag name: "4-Toekomst"
        /// </summary>
        public const string Diploma = "4-Toekomst";
    }
    
    /// <summary>
    /// Scene file names used with SceneManager.LoadScene() and LoadSceneAsync().
    /// </summary>
    public static class SceneFiles
    {
        /// <summary>
        /// Word Shooter scene file name: "WordShooter"
        /// </summary>
        public const string WordShooter = "WordShooter";
        
        /// <summary>
        /// Quiz scene file name: "QuizScene"
        /// </summary>
        public const string Quiz = "QuizScene";
        
        /// <summary>
        /// Beat Saber scene file name: "BeatSaber"
        /// </summary>
        public const string BeatSaber = "BeatSaber";
        
        /// <summary>
        /// Diploma scene file name: "Diploma"
        /// </summary>
        public const string Diploma = "Diploma";
        
        /// <summary>
        /// End scene file name: "EndScene"
        /// </summary>
        public const string EndScene = "EndScene";
        
        /// <summary>
        /// Virus Saber scene file name: "VirusSaber"
        /// </summary>
        public const string VirusSaber = "VirusSaber";
    }
    
    /// <summary>
    /// Video name constants used with FindVideoUrl().
    /// These match the "name" field in the JSON config file.
    /// </summary>
    public static class Videos
    {
        /// <summary>
        /// De Dijk intro video: "DDIntro"
        /// </summary>
        public const string DDIntro = "DDIntro";
        
        /// <summary>
        /// Sport video: "Sport"
        /// </summary>
        public const string Sport = "Sport";
        
        /// <summary>
        /// Ice skating rink video: "SchaatsBaan"
        /// </summary>
        public const string SchaatsBaan = "SchaatsBaan";
        
        /// <summary>
        /// Quiz intro video: "IntroQuiz"
        /// </summary>
        public const string IntroQuiz = "IntroQuiz";
        
        /// <summary>
        /// Quiz outro video: "OutroQuiz"
        /// </summary>
        public const string OutroQuiz = "OutroQuiz";
        
        /// <summary>
        /// Beat Saber intro video: "IntroBeatSaber"
        /// </summary>
        public const string IntroBeatSaber = "IntroBeatSaber";
        
        /// <summary>
        /// Beat Saber outro video: "OutroBeatSaber"
        /// </summary>
        public const string OutroBeatSaber = "OutroBeatSaber";
        
        /// <summary>
        /// Diploma intro video: "IntroDiploma"
        /// </summary>
        public const string IntroDiploma = "IntroDiploma";
        
        /// <summary>
        /// Diploma drone video: "DroneDiploma"
        /// </summary>
        public const string DroneDiploma = "DroneDiploma";
        
        /// <summary>
        /// Diploma outro video: "OutroDiploma"
        /// </summary>
        public const string OutroDiploma = "OutroDiploma";
        
        /// <summary>
        /// Vacation video: "Vakantie"
        /// </summary>
        public const string Vakantie = "Vakantie";
        
        /// <summary>
        /// Strand (beach) video: "Strand"
        /// </summary>
        public const string Strand = "Strand";
    }
    
    /// <summary>
    /// Audio name constants used with FindAudioUrl().
    /// These match the "name" field in the JSON config file.
    /// </summary>
    public static class Audio
    {
        /// <summary>
        /// Word game explanation audio: "explain"
        /// </summary>
        public const string Explain = "explain";
        
        /// <summary>
        /// Guitar background music (Happy Go Lucky): "guitar-music"
        /// </summary>
        public const string GuitarMusic = "guitar-music";
        
        /// <summary>
        /// Laser gun SFX: "laser-gun"
        /// </summary>
        public const string LaserGun = "laser-gun";
    }
    
    /// <summary>
    /// Volume configuration constants for audio balance.
    /// Default volumes can be adjusted here for fine-tuning.
    /// </summary>
    public static class Volume
    {
        /// <summary>
        /// Default music volume (0-1).
        /// </summary>
        public const float DefaultMusic = 1f;
        
        /// <summary>
        /// Default SFX volume (0-1).
        /// </summary>
        public const float DefaultSFX = 1f;
        
        /// <summary>
        /// Video audio volume settings (0-1).
        /// Adjust these to balance video audio levels.
        /// </summary>
        public static class Video
        {
            /// <summary>
            /// Schaats video audio volume (softer).
            /// </summary>
            public const float SchaatsBaan = 0.6f;
            
            /// <summary>
            /// Strand video audio volume (reduced to 60%).
            /// </summary>
            public const float Strand = 0.6f;
            
            /// <summary>
            /// Default video audio volume for videos without specific settings.
            /// </summary>
            public const float Default = 1f;
        }
        
        /// <summary>
        /// Scene-specific volume multipliers (0-1).
        /// Can be used to adjust overall audio levels per scene.
        /// </summary>
        public static class Scene
        {
            /// <summary>
            /// Default scene volume multiplier.
            /// </summary>
            public const float Default = 1f;
        }
    }
}
