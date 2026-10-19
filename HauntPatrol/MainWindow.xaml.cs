using System.Windows;
using System.Windows.Controls;
using HauntPatrol.Core;

namespace HauntPatrol;

public partial class MainWindow : Window
{
    const int MaxLogLines = 200;

    public HauntingController Controller { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        Controller.Log += line =>
        {
            LogList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
            if (LogList.Items.Count > MaxLogLines) LogList.Items.RemoveAt(MaxLogLines);
        };
        Controller.StateChanged += ShowState;
        Closed += (_, _) => Controller.Shutdown();

        // Settings are read after InitializeComponent so no handler runs against a half-built window.
        PrimaryOnly.Checked += (_, _) => Controller.AllMonitors = false;
        AllMonitors.Checked += (_, _) => Controller.AllMonitors = true;
        IntervalBox.SelectionChanged += (_, _) => ApplyInterval();
        SoundBox.Checked += (_, _) => Controller.Audio.Enabled = true;
        SoundBox.Unchecked += (_, _) => Controller.Audio.Enabled = false;
        DemoBox.Checked += (_, _) => Controller.DemoMode = true;
        DemoBox.Unchecked += (_, _) => Controller.DemoMode = false;
        ApplyInterval();

        Controller.ReportDiagnostics();
        ShowState();
    }

    void ApplyInterval()
    {
        if (IntervalBox.SelectedItem is ComboBoxItem { Tag: string seconds })
            Controller.Interval = TimeSpan.FromSeconds(double.Parse(seconds));
    }

    void ShowState()
    {
        StateText.Text = Controller.State + (Controller.AutoEnabled ? "  (hauntings on)" : "  (hauntings off)");
        StartButton.IsEnabled = !Controller.AutoEnabled;
        StopButton.IsEnabled = Controller.AutoEnabled || Controller.Busy;
    }

    void OnStart(object sender, RoutedEventArgs e) { Controller.StartHaunting(); ShowState(); }
    void OnStop(object sender, RoutedEventArgs e) { Controller.StopHaunting(); ShowState(); }
    void OnTriggerSkeleton(object sender, RoutedEventArgs e) => Controller.StartEncounter(EncounterKind.Skeleton);
    void OnTriggerCat(object sender, RoutedEventArgs e) => Controller.StartEncounter(EncounterKind.Cat);
    void OnTriggerGlass(object sender, RoutedEventArgs e) => Controller.StartPenalty(PenaltyKind.Glass);
    void OnTriggerSlime(object sender, RoutedEventArgs e) => Controller.StartPenalty(PenaltyKind.Slime);
    void OnTriggerGhost(object sender, RoutedEventArgs e) => Controller.StartEncounter(EncounterKind.Ghost);
    void OnTriggerZombie(object sender, RoutedEventArgs e) => Controller.StartEncounter(EncounterKind.Zombie);
    void OnTriggerFog(object sender, RoutedEventArgs e) => Controller.StartPenalty(PenaltyKind.Fog);
    void OnTriggerInfection(object sender, RoutedEventArgs e) => Controller.StartPenalty(PenaltyKind.Infection);
    void OnForceWin(object sender, RoutedEventArgs e) => Controller.ForceOutcome(EncounterResult.Victory);
    void OnForceFail(object sender, RoutedEventArgs e) => Controller.ForceOutcome(EncounterResult.Failure);
    void OnExit(object sender, RoutedEventArgs e) => Close();
}
