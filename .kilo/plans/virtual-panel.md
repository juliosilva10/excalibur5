# Plano: Painel Virtual

## Visão Geral

Implementar um painel "Virtual" que substitui o espaço do painel Recover no `ContractPanelView`. O usuário clica em L (Loss, vermelho) ou W (Win, verde) para simular resultados de entradas. Após uma sequência definida (ex: "LL"), um sinal é disparado indicando que a próxima entrada manual será com dinheiro real.

---

## 1. Novo arquivo: `ViewModels/VirtualViewModel.cs`

```csharp
namespace Excalibur5.ViewModels;

public partial class VirtualViewModel : ObservableObject
{
    [ObservableProperty] private bool _isVirtualVisible;
    [ObservableProperty] private string _targetSequence = "LL";
    [ObservableProperty] private string _sequenceHistory = string.Empty;
    [ObservableProperty] private bool _isSequenceComplete;

    public string ProgressDisplay => string.Join(" → ", SequenceHistory.ToCharArray());
    public bool IsIdle => string.IsNullOrEmpty(SequenceHistory);

    [RelayCommand] private void ToggleVirtual()
    {
        IsVirtualVisible = !IsVirtualVisible;
        if (!IsVirtualVisible) ResetSequence();
    }

    [RelayCommand] private void RecordWin()
    {
        SequenceHistory += "W";
        CheckSequence();
    }

    [RelayCommand] private void RecordLoss()
    {
        SequenceHistory += "L";
        CheckSequence();
    }

    [RelayCommand] private void ResetSequence()
    {
        SequenceHistory = string.Empty;
        IsSequenceComplete = false;
    }

    private void CheckSequence()
    {
        IsSequenceComplete = SequenceHistory.EndsWith(TargetSequence, StringComparison.OrdinalIgnoreCase);
        if (IsSequenceComplete) SequenceHistory = string.Empty;
        OnPropertyChanged(nameof(ProgressDisplay));
        OnPropertyChanged(nameof(IsIdle));
    }

    partial void OnTargetSequenceChanged(string value)
    {
        CheckSequence();
    }
}
```

## 2. Modificar `ViewModels/MainViewModel.cs`

### 2.1 Substituir propriedade `_isVirtualPanelVisible` por VirtualViewModel

```csharp
// ANTES
[ObservableProperty] private bool    _isVirtualPanelVisible;

// DEPOIS
public VirtualViewModel Virtual { get; } = new();
```

### 2.2 Atualizar `ToggleVirtualPanel` command

Manter o comando no MainViewModel (para consistência com o binding existente no SidebarControl), mas delegar ao VirtualViewModel:

```csharp
[RelayCommand]
private void ToggleVirtualPanel()
{
    Virtual.ToggleVirtualCommand.Execute(null);
}
```

### 2.3 Atualizar referências a `IsVirtualPanelVisible`

Substituir todas as ocorrências de `IsVirtualPanelVisible = false` por `Virtual.IsVirtualVisible = false`:

- Line 91
- Line 100
- Line 119
- Line 144
- Line 156
- Line 168
- Line 180
- Line 185-186 (handler `IsVirtualPanelVisible` → `Virtual.IsVirtualVisible`)

### 2.4 Remover propriedades computadas antigas

Remover ou manter `IsVirtual`, `AccountTypeLabel`, `ShowVirtualGlow`, `ShowRealGlow` — estas são sobre o tipo de conta (real/virtual), não sobre o painel Virtual. Mantê-las pois são independentes.

## 3. Modificar `Views/Controls/SidebarControl.xaml`

### 3.1 Atualizar `DataTrigger` do botão Virtual

```xml
<!-- ANTES -->
<DataTrigger Binding="{Binding IsVirtualPanelVisible}" Value="True">

<!-- DEPOIS -->
<DataTrigger Binding="{Binding Virtual.IsVirtualVisible}" Value="True">
```

## 4. Modificar `Views/Controls/ContractPanelView.xaml`

### 4.1 Adicionar painel Virtual APÓS o painel Recover (linha ~910)

Inserir após o fechamento do StackPanel do Recover (antes de fechar o Grid de conteúdo):

```xml
<!-- VIRTUAL PANEL (visible when Virtual active, spans cols 0-2) -->
<StackPanel Grid.Column="0" Grid.ColumnSpan="3"
            DataContext="{Binding DataContext.Virtual, RelativeSource={RelativeSource AncestorType=Window}}">
    <StackPanel.Style>
        <Style TargetType="StackPanel">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsVirtualVisible}" Value="True">
                    <Setter Property="Visibility" Value="Visible"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </StackPanel.Style>

    <TextBlock Text="Entrada Virtual"
               Foreground="#f0c000" FontSize="12" FontWeight="SemiBold"
               Margin="0,0,0,8"/>

    <!-- Sequência alvo -->
    <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
        <TextBlock Text="Sequência alvo:"
                   Foreground="#90b5c9" FontSize="10"
                   VerticalAlignment="Center" Margin="0,0,8,0"/>
        <TextBox Style="{StaticResource DerivTextBox}"
                 Text="{Binding TargetSequence, UpdateSourceTrigger=PropertyChanged}"
                 Height="26" Width="80"
                 HorizontalAlignment="Left"
                 MaxLength="10"
                 FontFamily="Consolas" FontSize="12"
                 Foreground="#f0c000"/>
    </StackPanel>

    <!-- Display de progresso -->
    <TextBlock Text="{Binding ProgressDisplay}"
               FontFamily="Consolas" FontSize="12" FontWeight="Bold"
               Foreground="#e0eef8" Margin="0,0,0,8"
               Visibility="{Binding IsIdle, Converter={StaticResource InverseBoolToVis}}"/>

    <!-- Status / sinal -->
    <Border CornerRadius="4" Padding="8,4"
            Background="#1a3a2a"
            BorderBrush="#34c759" BorderThickness="1"
            Margin="0,0,0,8"
            Visibility="{Binding IsSequenceComplete, Converter={StaticResource BoolToVis}}">
        <TextBlock Text="✅ SINAL: PRÓXIMA ENTRADA SERÁ REAL!"
                   Foreground="#34c759" FontSize="11" FontWeight="Bold"/>
    </Border>

    <!-- Botões W / L -->
    <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
        <Button Command="{Binding RecordWinCommand}"
                Content="W" Width="60" Height="32"
                Style="{StaticResource DerivCallButtonStyle}"
                FontSize="13" FontWeight="Bold"
                Margin="0,0,10,0"/>
        <Button Command="{Binding RecordLossCommand}"
                Content="L" Width="60" Height="32"
                Style="{StaticResource DerivPutButtonStyle}"
                FontSize="13" FontWeight="Bold"/>
    </StackPanel>

    <!-- Reset -->
    <Button Command="{Binding ResetSequenceCommand}"
            Content="Reiniciar" Width="80" Height="24"
            FontSize="9"
            Style="{StaticResource DerivToggleButtonStyle}"
            HorizontalAlignment="Left"/>
</StackPanel>
```

### 4.2 Adicionar também um tab header "Virtual" (como Recover/Bot)

No cabeçalho do ContractPanelView (após o Bot tab, ~line 110), adicionar:

```xml
<!-- Virtual tab (when active) -->
<Border CornerRadius="4"
        BorderBrush="#f0c000" BorderThickness="1"
        Padding="10,3">
    <Border.Style>
        <Style TargetType="Border">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding DataContext.Virtual.IsVirtualVisible, RelativeSource={RelativeSource AncestorType=Window}}" Value="True">
                    <Setter Property="Visibility" Value="Visible"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
    <Border.Background>
        <LinearGradientBrush StartPoint="0,1" EndPoint="0,0">
            <GradientStop Color="#30200f" Offset="0"/>
            <GradientStop Color="#4a3a1a" Offset="0.85"/>
        </LinearGradientBrush>
    </Border.Background>
    <TextBlock Text="Virtual"
               Foreground="#f0c000" FontSize="11" FontWeight="SemiBold"/>
</Border>
```

### 4.3 Ocultar conteúdo normal quando Virtual ativo

Adicionar DataTrigger para `Virtual.IsVirtualVisible` nos mesmos lugares onde `Recover.IsRecoverVisible` e `Strategy.IsBotVisible` já ocultam o conteúdo:

- Header Call/Put tab (linha 41): adicionar trigger `Virtual.IsVirtualVisible = True` → Collapsed
- Contract type selector (linha 120): adicionar trigger
- Left column (linha 166): adicionar trigger  
- Right column (linha 329): adicionar trigger

---

## 5. Atualizar closures de painéis em MainViewModel

Adicionar no handler de `Virtual.IsVirtualVisible` (substituir a lógica antiga de `IsVirtualPanelVisible`):

```csharp
// no construtor do MainViewModel, adicionar ao lado dos outros handlers de painel:
Virtual.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(Virtual.IsVirtualVisible) && Virtual.IsVirtualVisible)
    {
        Markets.IsMarketsVisible = false;
        Log.IsLogVisible = false;
        Strategy.IsBotVisible = false;
        Recover.IsRecoverVisible = false;
        History.IsHistoryVisible = false;
        Performance.IsPerformanceVisible = false;
    }
};
```

Isso garante que abrir o Virtual fecha os outros painéis, assim como os outros painéis fecham o Virtual.<｜end▁of▁thinking｜>

<｜｜DSML｜｜tool_calls>
<｜｜DSML｜｜invoke name="plan_exit">
<｜｜DSML｜｜parameter name="path" string="true">C:\Users\Júlio César\Downloads\excalibur5\.kilo\plans\virtual-panel.md

## Observações

- Os botões W e L reutilizam os estilos `DerivCallButtonStyle` (verde) e `DerivPutButtonStyle` (vermelho) existentes em `App.xaml`, garantindo consistência visual.
- O layout do painel segue exatamente o padrão do Recover: `Grid.Column="0" Grid.ColumnSpan="3"` com `Visibility` controlada por trigger.
- "Sem delay de tick" significa que, quando o sinal estiver ativo, o usuário clica Call/Put e a compra é executada usando a proposal já disponível (sem esperar novo tick).
- O VirtualViewModel não intercepta o fluxo de compra — ele apenas sinaliza visualmente. A decisão de comprar é do usuário.
