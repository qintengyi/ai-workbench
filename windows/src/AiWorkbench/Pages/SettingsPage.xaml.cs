using System.Linq;
using System.Threading.Tasks;
using AiWorkbench.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AiWorkbench.Pages;

/// <summary>Provider 配置 UI（暴露第 8 条所有字段）。</summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        _ = LoadListAsync();
    }

    private async Task LoadListAsync()
    {
        var list = await MainWindow.ProviderStore.LoadAsync();
        ProviderList.ItemsSource = list;
        if (list.Count > 0) ProviderList.SelectedIndex = 0;
    }

    private void Provider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderList.SelectedItem is Provider p) FillForm(p);
    }

    private void FillForm(Provider p)
    {
        IdBox.Text = p.Id;
        NameBox.Text = p.Name;
        VendorBox.Text = p.Vendor;
        UrlBox.Text = p.Url;
        KeyBox.Password = p.ApiKey;
        MaxInBox.Value = p.MaxInputTokens;
        OutBox.Value = p.MaxOutputTokens;
        ToolCallToggle.IsOn = p.SupportsToolCall;
        ReasoningOnlyToggle.IsOn = p.SupportsReasoning;
        ImagesToggle.IsOn = p.SupportsImages;
        CustomProtoToggle.IsOn = p.UseCustomProtocol;
        ApiFormatCombo.SelectedItem = string.IsNullOrEmpty(p.ApiFormat) ? "openai" : p.ApiFormat;
        ReasoningModeBox.Text = p.ReasoningMode;
        AllowDisableToggle.IsOn = p.AllowDisableReasoning;
        DefaultEffortBox.Text = p.Reasoning.DefaultEffort;
        SupportedEffortsBox.Text = string.Join(", ", p.Reasoning.SupportedEfforts);
        AuxToggle.IsOn = p.IsAuxiliary;
        AuxForBox.Text = p.AuxiliaryFor ?? string.Empty;
    }

    private Provider ReadForm()
    {
        var p = new Provider
        {
            Id = IdBox.Text.Trim(),
            Name = string.IsNullOrEmpty(NameBox.Text) ? IdBox.Text.Trim() : NameBox.Text.Trim(),
            Vendor = VendorBox.Text.Trim(),
            Url = UrlBox.Text.Trim(),
            ApiKey = KeyBox.Password,
            MaxInputTokens = (int)MaxInBox.Value,
            MaxOutputTokens = (int)OutBox.Value,
            SupportsToolCall = ToolCallToggle.IsOn,
            SupportsReasoning = ReasoningOnlyToggle.IsOn,
            SupportsImages = ImagesToggle.IsOn,
            UseCustomProtocol = CustomProtoToggle.IsOn,
            ApiFormat = ApiFormatCombo.SelectedItem?.ToString() == "anthropic" ? "anthropic" : "openai",
            ReasoningMode = ReasoningModeBox.Text.Trim(),
            AllowDisableReasoning = AllowDisableToggle.IsOn,
            IsAuxiliary = AuxToggle.IsOn,
            AuxiliaryFor = string.IsNullOrEmpty(AuxForBox.Text) ? null : AuxForBox.Text.Trim(),
        };
        p.Reasoning.DefaultEffort = string.IsNullOrEmpty(DefaultEffortBox.Text) ? "medium" : DefaultEffortBox.Text.Trim();
        p.Reasoning.SupportedEfforts = SupportedEffortsBox.Text
            .Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
            .ToList();
        if (p.Reasoning.SupportedEfforts.Count == 0)
            p.Reasoning.SupportedEfforts = new() { "medium" };
        return p;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var p = ReadForm();
        if (string.IsNullOrEmpty(p.Id))
        {
            await new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "提示",
                Content = "模型 ID 不能为空",
                CloseButtonText = "知道了",
            }.ShowAsync();
            return;
        }
        await MainWindow.ProviderStore.UpsertAsync(p);
        await LoadListAsync();
        // 重新选中刚保存的 provider
        for (int i = 0; i < ProviderList.Items.Count; i++)
        {
            if (ProviderList.Items[i] is Provider item && item.Id == p.Id)
            {
                ProviderList.SelectedIndex = i;
                break;
            }
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ProviderList.SelectedItem is not Provider p) return;
        await MainWindow.ProviderStore.DeleteAsync(p.Id);
        await LoadListAsync();
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        ProviderList.SelectedIndex = -1;
        FillForm(new Provider());
        IdBox.Focus(FocusState.Programmatic);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (ProviderList.SelectedItem is Provider p) FillForm(p);
    }
}
