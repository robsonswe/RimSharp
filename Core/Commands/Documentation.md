# RimSharp Command System Technical Documentation

## Table of Contents
1. [Overview](#overview)
2. [Command Interfaces](#command-interfaces)
   - [IDelegateCommand](#idelegatecommand)
   - [IDelegateCommand&lt;T&gt;](#idelegatecommendt)
3. [Command Implementations](#command-implementations)
   - [DelegateCommand&lt;T&gt;](#delegatecommendt)
   - [DelegateCommand](#delegatecommand)
   - [AsyncDelegateCommand&lt;T&gt;](#asyncdelegatecommendt)
   - [AsyncDelegateCommand](#asyncdelegatecommand)
   - [RelayCommand](#relaycommand)
   - [AsyncRelayCommand](#asyncrelaycommand)
4. [Composite Commands](#composite-commands)
   - [CompositeCommand](#compositecommand)
5. [Command Services](#command-services)
   - [ICommandInitializer](#icommandinitializer)
   - [IModCommandService](#imodcommandservice)
   - [ModCommandService](#modcommandservice)
6. [Event Aggregation](#event-aggregation)
   - [IEventAggregator](#ieventaggregator)
   - [SubscriptionToken](#subscriptiontoken)
   - [WeakEventAggregator](#weakeventaggregator)
7. [Implementation Examples](#implementation-examples)
   - [Basic Command Usage](#basic-command-usage)
   - [Async Command Usage](#async-command-usage)
   - [Composite Command Usage](#composite-command-usage)
   - [Event Aggregator Usage](#event-aggregator-usage)
   - [Command Service Usage](#command-service-usage)

## Overview

The RimSharp Command System provides a comprehensive implementation of the Command pattern for WPF applications. It offers a robust set of components that enable the creation of commands that can be bound to UI elements and executed in response to user actions. The system includes support for:

- Synchronous and asynchronous command execution
- Type-safe command parameters
- Command composition
- Property change observation
- Event aggregation
- Command registration and management

This document provides detailed technical information about each component in the system and how they can be used together to create a flexible and maintainable application architecture.

## Command Interfaces

### IDelegateCommand

**Namespace**: `RimSharp.Core.Commands.Base`

**Description**: Base interface for delegate commands that extends the WPF `ICommand` interface with additional functionality.

**Members**:

```csharp
void RaiseCanExecuteChanged();
```

| Method | Description | Parameters | Returns |
|--------|-------------|------------|---------|
| RaiseCanExecuteChanged | Raises the CanExecuteChanged event to force a re-evaluation of command execution ability. | None | void |

**Implementation Example**:
```csharp
public class MyCommand : IDelegateCommand
{
    public event EventHandler CanExecuteChanged;
    
    public bool CanExecute(object parameter) => true;
    
    public void Execute(object parameter) { /* Implementation */ }
    
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
```

### IDelegateCommand&lt;T&gt;

**Namespace**: `RimSharp.Core.Commands.Base`

**Description**: Generic interface for delegate commands that extends the WPF `ICommand` interface with additional functionality and provides type safety for parameters.

**Members**:

```csharp
void RaiseCanExecuteChanged();
```

| Method | Description | Parameters | Returns |
|--------|-------------|------------|---------|
| RaiseCanExecuteChanged | Raises the CanExecuteChanged event to force a re-evaluation of command execution ability. | None | void |

**Implementation Example**:
```csharp
public class MyTypedCommand<T> : IDelegateCommand<T>
{
    public event EventHandler CanExecuteChanged;
    
    public bool CanExecute(object parameter) => parameter is T;
    
    public void Execute(object parameter)
    {
        if (parameter is T typedParam)
        {
            // Use typedParam safely
        }
    }
    
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
```

## Command Implementations

### DelegateCommand&lt;T&gt;

**Namespace**: `RimSharp.Core.Commands.Base`

**Description**: A generic command implementation that provides type safety for its parameters and delegates command functionality to provided delegates.

**Constructors**:

```csharp
DelegateCommand(Action<T> execute, Predicate<T> canExecute = null)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| execute | Action&lt;T&gt; | The action to execute when the command is invoked. |
| canExecute | Predicate&lt;T&gt; | Optional predicate that determines whether the command can execute. |

**Methods**:

| Method | Description | Parameters | Returns |
|--------|-------------|------------|---------|
| CanExecute | Determines whether the command can execute with the provided parameter. | object parameter | bool |
| Execute | Executes the command with the provided parameter. | object parameter | void |
| RaiseCanExecuteChanged | Raises the CanExecuteChanged event to force a re-evaluation of command execution ability. | None | void |
| ObservesProperty | Sets up property change observation to automatically raise CanExecuteChanged when a property changes. | INotifyPropertyChanged owner, string propertyName | DelegateCommand&lt;T&gt; |

**Properties**:

| Property | Type | Description |
|----------|------|-------------|
| CanExecuteChanged (event) | EventHandler | Event that is raised when the ability to execute the command changes. |

**Implementation Example**:
```csharp
// In a ViewModel
public class MyViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    
    private bool _canPerformAction;
    public bool CanPerformAction
    {
        get => _canPerformAction;
        set
        {
            _canPerformAction = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanPerformAction)));
        }
    }
    
    private DelegateCommand<string> _myCommand;
    public DelegateCommand<string> MyCommand => _myCommand ??= new DelegateCommand<string>(
        execute: (param) => { /* Perform action with param */ },
        canExecute: (param) => CanPerformAction && !string.IsNullOrEmpty(param)
    ).ObservesProperty(this, nameof(CanPerformAction));
}
```

### DelegateCommand

**Namespace**: `RimSharp.Core.Commands.Base`

**Description**: Non-generic delegate command that inherits from DelegateCommand&lt;object&gt; to simplify command creation when type safety is not required.

**Constructors**:

```csharp
DelegateCommand(Action execute, Func<bool> canExecute = null)
DelegateCommand(Action<object> execute, Func<object, bool> canExecute = null)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| execute | Action or Action&lt;object&gt; | The action to execute when the command is invoked. |
| canExecute | Func&lt;bool&gt; or Func&lt;object, bool&gt; | Optional function that determines whether the command can execute. |

**Methods**:

| Method | Description | Parameters | Returns |
|--------|-------------|------------|---------|
| ObservesProperty | Sets up property change observation to automatically raise CanExecuteChanged when a property changes. | INotifyPropertyChanged owner, string propertyName | DelegateCommand |

**Implementation Example**:
```csharp
// In a ViewModel
public class MyViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    
    private bool _canPerformAction;
    public bool CanPerformAction
    {
        get => _canPerformAction;
        set
        {
            _canPerformAction = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanPerformAction)));
        }
    }
    
    private DelegateCommand _myCommand;
    public DelegateCommand MyCommand => _myCommand ??= new DelegateCommand(
        execute: () => { /* Perform action */ },
        canExecute: () => CanPerformAction
    ).ObservesProperty(this, nameof(CanPerformAction));
}
```

### AsyncDelegateCommand&lt;T&gt;

**Namespace**: `RimSharp.Core.Commands.Base`

**Description**: A generic asynchronous command implementation that provides type safety for its parameters and delegates command functionality to provided async delegates.

**Constructors**:

```csharp
AsyncDelegateCommand(Func<T, CancellationToken, Task> execute, Predicate<T> canExecute = null)
AsyncDelegateCommand(Func<T, Task> execute, Predicate<T> canExecute = null)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| execute | Func&lt;T, CancellationToken, Task&gt; or Func&lt;T, Task&gt; | The async function to execute when the command is invoked. |
| canExecute | Predicate&lt;T&gt; | Optional predicate that determines whether the command can execute. |

**Methods**:

| Method | Description | Parameters | Returns |
|--------|-------------|------------|---------|
| CanExecute | Determines whether the command can execute with the provided parameter. | object parameter | bool |
| Execute | Executes the command with the provided parameter asynchronously. | object parameter | void |
| RaiseCanExecuteChanged | Raises the CanExecuteChanged event to force a re-evaluation of command execution ability. | None | void |
| ObservesProperty | Sets up property change observation to automatically raise CanExecuteChanged when a property changes. | INotifyPropertyChanged owner, string propertyName | AsyncDelegateCommand&lt;T&gt; |

**Properties**:

| Property | Type | Description |
|----------|------|-------------|
| CanExecuteChanged (event) | EventHandler | Event that is raised when the ability to execute the command changes. |

**Implementation Example**:
```csharp
// In a ViewModel
public class MyViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
        }
    }
    
    private AsyncDelegateCommand<string> _loadDataCommand;
    public AsyncDelegateCommand<string> LoadDataCommand => _loadDataCommand ??= new AsyncDelegateCommand<string>(
        execute: async (param) => {
            IsBusy = true;
            try {
                await LoadDataAsync(param);
            }
            finally {
                IsBusy = false;
            }
        },
        canExecute: (param) => !IsBusy && !string.IsNullOrEmpty(param)
    ).ObservesProperty(this, nameof(IsBusy));
    
    private async Task LoadDataAsync(string parameter)
    {
        // Asynchronous data loading implementation
        await Task.Delay(1000); // Simulating work
    }
}
```

### AsyncDelegateCommand

**Namespace**: `RimSharp.Core.Commands.Base`

**Description**: Non-generic asynchronous delegate command that inherits from AsyncDelegateCommand&lt;object&gt; to simplify async command creation when type safety is not required.

**Constructors**:

```csharp
AsyncDelegateCommand(Func<Task> execute, Func<bool> canExecute = null)
AsyncDelegateCommand(Func<object, Task> execute, Func<object, bool> canExecute = null)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| execute | Func&lt;Task&gt; or Func&lt;object, Task&gt; | The async function to execute when the command is invoked. |
| canExecute | Func&lt;bool&gt; or Func&lt;object, bool&gt; | Optional function that determines whether the command can execute. |

**Methods**:

| Method | Description | Parameters | Returns |
|--------|-------------|------------|---------|
| ObservesProperty | Sets up property change observation to automatically raise CanExecuteChanged when a property changes. | INotifyPropertyChanged owner, string propertyName | AsyncDelegateCommand |

**Implementation Example**:
```csharp
// In a ViewModel
public class MyViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
        }
    }
    
    private AsyncDelegateCommand _saveCommand;
    public AsyncDelegateCommand SaveCommand => _saveCommand ??= new AsyncDelegateCommand(
        execute: async () => {
            IsBusy = true;
            try {
                await SaveDataAsync();
            }
            finally {
                IsBusy = false;
            }
        },
        canExecute: () => !IsBusy
    ).ObservesProperty(this, nameof(IsBusy));
    
    private async Task SaveDataAsync()
    {
        // Asynchronous save implementation
        await Task.Delay(1000); // Simulating work
    }
}
```

### RelayCommand

**Namespace**: `RimSharp.Core.Commands.Base`

**Description**: A command implementation that relays functionality to delegate methods, similar to DelegateCommand but with additional functionality.

**Constructors**:

```csharp
RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
RelayCommand(Action execute, Func<bool> canExecute = null)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| execute | Action&lt;object&gt; or Action | The action to execute when the command is invoked. |
| canExecute | Func&lt;object, bool&gt; or Func&lt;bool&gt; | Optional function that determines whether the command can execute. |

**Methods**:

| Method | Description | Parameters | Returns |
|--------|-------------|------------|---------|
| CanExecute | Determines whether the command can execute with the provided parameter. | object parameter | bool |
| Execute | Executes the command with the provided parameter. | object parameter | void |
| RaiseCanExecuteChanged | Raises the CanExecuteChanged event to force a re-evaluation of command execution ability. | None | void |
| ObservesProperty | Sets up property change observation to automatically raise CanExecuteChanged when a property changes. | INotifyPropertyChanged owner, string propertyName | RelayCommand |
| ObservesProperties | Sets up property change observation to automatically raise CanExecuteChanged when multiple properties change. | INotifyPropertyChanged owner, params string[] propertyNames | RelayCommand |

**Properties**:

| Property | Type | Description |
|----------|------|-------------|
| CanExecuteChanged (event) | EventHandler | Event that is raised when the ability to execute the command changes. |

**Implementation Example**:
```csharp
// In a ViewModel
public class MyViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    
    private bool _canEdit;
    public bool CanEdit
    {
        get => _canEdit;
        set
        {
            _canEdit = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEdit)));
        }
    }
    
    private bool _hasChanges;
    public bool HasChanges
    {
        get => _hasChanges;
        set
        {
            _hasChanges = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasChanges)));
        }
    }
    
    private RelayCommand _saveCommand;
    public RelayCommand SaveCommand => _saveCommand ??= new RelayCommand(
        execute: () => { /* Save changes */ },
        canExecute: () => CanEdit && HasChanges
    ).ObservesProperties(this, nameof(CanEdit), nameof(HasChanges));
}
```

### AsyncRelayCommand

**Namespace**: `RimSharp.Core.Commands.Base`

**Description**: An asynchronous command implementation that relays functionality to delegate methods.

**Constructors**:

```csharp
AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool> canExecute = null)
AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| execute | Func&lt;CancellationToken, Task&gt; or Func&lt;Task&gt; | The async function to execute when the command is invoked. |
| canExecute | Func&lt;bool&gt; | Optional function that determines whether the command can execute. |

**Methods**:

| Method | Description | Parameters | Returns |
|--------|-------------|------------|---------|
| CanExecute | Determines whether the command can execute with the provided parameter. | object parameter | bool |
| Execute | Executes the command with the provided parameter asynchronously. | object parameter | void |
| RaiseCanExecuteChanged | Raises the CanExecuteChanged event to force a re-evaluation of command execution ability. | None | void |
| ObservesProperty | Sets up property change observation to automatically raise CanExecuteChanged when a property changes. | INotifyPropertyChanged owner, string propertyName | AsyncRelayCommand |

**Properties**:

| Property | Type | Description |
|----------|------|-------------|
| CanExecuteChanged (event) | EventHandler | Event that is raised when the ability to execute the command changes. |

**Implementation Example**:
```csharp
// In a ViewModel
public class MyViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
        }
    }
    
    private AsyncRelayCommand _refreshCommand;
    public AsyncRelayCommand RefreshCommand => _refreshCommand ??= new AsyncRelayCommand(
        execute: async (cancellationToken) => {
            IsBusy = true;
            try {
                await RefreshDataAsync(cancellationToken);
            }
            finally {
                IsBusy = false;
            }
        },
        canExecute: () => !IsBusy
    ).ObservesProperty(this, nameof(IsBusy));
    
    private async Task RefreshDataAsync(CancellationToken cancellationToken)
    {
        // Asynchronous refresh implementation with cancellation support
        await Task.Delay(1000, cancellationToken); // Simulating work
    }
}
```

## Composite Commands

### CompositeCommand

**Namespace**: `RimSharp.Core.Commands.Composite`

**Description**: A command that aggregates multiple commands and executes them as one.

**Constructors**:

```csharp
CompositeCommand(bool monitorCommandActivity = true)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| monitorCommandActivity | bool | If true, each command will be monitored for its CanExecuteChanged event. When any command's CanExecuteChanged fires, the CompositeCommand's CanExecuteChanged will fire too. |

**Methods**:

| Method | Description | Parameters | Returns |
|--------|-------------|------------|---------|
| RegisterCommand | Registers a command with the composite command. | ICommand command | void |
| UnregisterCommand | Unregisters a command from the composite command. | ICommand command | void |
| CanExecute | Determines if the composite command can execute. Returns true if all registered commands can execute. | object parameter | bool |
| Execute | Executes all registered commands that can execute with the provided parameter. | object parameter | void |
| RaiseCanExecuteChanged | Raises the CanExecuteChanged event. | None | void |

**Properties**:

| Property | Type | Description |
|----------|------|-------------|
| CanExecuteChanged (event) | EventHandler | Event that is raised when the ability to execute the command changes. |
| RegisteredCommands | IReadOnlyList&lt;ICommand&gt; | Gets the list of registered commands. |

**Implementation Example**:
```csharp
// In a ViewModel
public class MyViewModel
{
    private readonly ICommand _saveDocumentCommand;
    private readonly ICommand _updateHistoryCommand;
    private readonly ICommand _notifyUserCommand;
    
    private CompositeCommand _saveAllCommand;
    public CompositeCommand SaveAllCommand => _saveAllCommand ??= CreateSaveAllCommand();
    
    private CompositeCommand CreateSaveAllCommand()
    {
        var composite = new CompositeCommand();
        composite.RegisterCommand(_saveDocumentCommand);
        composite.RegisterCommand(_updateHistoryCommand);
        composite.RegisterCommand(_notifyUserCommand);
        return composite;
    }
    
    // Later, to execute all commands at once:
    // SaveAllCommand.Execute(parameter);
}
```

## Command Services

### ICommandInitializer

**Namespace**: `RimSharp.Core.Services.Commanding`

**Description**: Interface for command initializers that register commands with the command service.

**Methods**:

| Method | Description | Parameters | Returns |
|--------|-------------|------------|---------|
| Initialize | Initializes commands and registers them with the command service. | IModCommandService commandService | void |

**Implementation Example**:
```csharp
public class AppCommandInitializer : ICommandInitializer
{
    private readonly IEventAggregator _eventAggregator;
    
    public AppCommandInitializer(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
    }
    
    public void Initialize(IModCommandService commandService)
    {
        // Register individual commands
        commandService.RegisterCommand("SaveCommand", new DelegateCommand(
            execute: () => { /* Save implementation */ }
        ));
        
        // Register composite commands
        var projectCommandsComposite = new CompositeCommand();
        commandService.RegisterCompositeCommand("ProjectCommands", projectCommandsComposite);
        
        // Add commands to composite
        commandService.AddToCompositeCommand("ProjectCommands", "SaveCommand");
    }
}
```

### IModCommandService

**Namespace**: `RimSharp.Core.Services.Commanding`

**Description**: Interface for a service that manages commands for the application.

**Methods**:

| Method | Description | Parameters | Returns |
|--------|-------------|------------|---------|
| RegisterCommand | Registers a command with the service. | string commandName, ICommand command | void |
| RegisterCompositeCommand | Registers a composite command with the service. | string commandName, CompositeCommand command | void |
| GetCommand | Gets a command from the service. | string commandName | ICommand |
| GetCompositeCommand | Gets a composite command from the service. | string commandName | CompositeCommand |
| AddToCompositeCommand | Adds a command to a composite command. | string compositeName, ICommand command | void |
| AddToCompositeCommand | Adds a named command to a composite command. | string compositeName, string commandName | void |
| ContainsCommand | Determines whether a command is registered with the service. | string commandName | bool |

**Implementation Example**:
```csharp
// In a bootstrapper or app startup class
public class AppBootstrapper
{
    private readonly IModCommandService _commandService;
    private readonly IEnumerable<ICommandInitializer> _commandInitializers;
    
    public AppBootstrapper(IModCommandService commandService, IEnumerable<ICommandInitializer> commandInitializers)
    {
        _commandService = commandService;
        _commandInitializers = commandInitializers;
    }
    
    public void Initialize()
    {
        // Initialize all command initializers
        foreach (var initializer in _commandInitializers)
        {
            initializer.Initialize(_commandService);
        }
        
        // Later, commands can be accessed by name
        if (_commandService.ContainsCommand("SaveCommand"))
        {
            var saveCommand = _commandService.GetCommand("SaveCommand");
            // Use the command
        }
    }
}
```

### ModCommandService

**Namespace**: `RimSharp.Core.Services.Commanding`

**Description**: A service that manages commands for the application, implementing IModCommandService.

**Constructors**:

```csharp
ModCommandService()
```

**Methods**:

| Method | Description | Parameters | Returns |
|--------|-------------|------------|---------|
| RegisterCommand | Registers a command with the service. | string commandName, ICommand command | void |
| RegisterCompositeCommand | Registers a composite command with the service. | string commandName, CompositeCommand command | void |
| GetCommand | Gets a command from the service. | string commandName | ICommand |
| GetCompositeCommand | Gets a composite command from the service. | string commandName | CompositeCommand |
| AddToCompositeCommand | Adds a command to a composite command. | string compositeName, ICommand command | void |
| AddToCompositeCommand | Adds a named command to a composite command. | string compositeName, string commandName | void |
| ContainsCommand | Determines whether a command is registered with the service. | string commandName | bool |

**Implementation Example**:
```csharp
// Registration in a DI container
public void ConfigureServices(IServiceCollection services)
{
    // Register the command service as a singleton
    services.AddSingleton<IModCommandService, ModCommandService>();
    
    // Register command initializers
    services.AddTransient<ICommandInitializer, NavigationCommandInitializer>();
    services.AddTransient<ICommandInitializer, DocumentCommandInitializer>();
}

// Usage in a view model
public class MainViewModel
{
    private readonly IModCommandService _commandService;
    
    public MainViewModel(IModCommandService commandService)
    {
        _commandService = commandService;
    }
    
    public ICommand SaveCommand => _commandService.GetCommand("SaveCommand");
    public ICommand PrintCommand => _commandService.GetCommand("PrintCommand");
    public ICommand DocumentCommands => _commandService.GetCompositeCommand("DocumentCommands");
}
```

## Event Aggregation

### IEventAggregator

**Namespace**: `RimSharp.Core.Commands.Aggregators`

**Description**: Interface for an event aggregator that allows publishing and subscribing to events.

**Methods**:

| Method | Description | Parameters | Returns |
|--------|-------------|------------|---------|
| Publish&lt;TEvent&gt; | Publishes an event to all subscribers. | TEvent eventToPublish | void |
| Subscribe&lt;TEvent&gt; | Subscribes to an event. | Action&lt;TEvent&gt; action | SubscriptionToken |
| Unsubscribe&lt;TEvent&gt; | Unsubscribes from an event. | SubscriptionToken token | void |

**Implementation Example**:
```csharp
// Define an event
public class DataLoadedEvent
{
    public object Data { get; }
    
    public DataLoadedEvent(object data)
    {
        Data = data;
    }
}

// Subscribe to an event
public class DataViewModel
{
    private readonly IEventAggregator _eventAggregator;
    private SubscriptionToken _dataLoadedToken;
    
    public DataViewModel(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
        _dataLoadedToken = _eventAggregator.Subscribe<DataLoadedEvent>(OnDataLoaded);
    }
    
    private void OnDataLoaded(DataLoadedEvent evt)
    {
        // Handle the event
        Console.WriteLine($"Data loaded: {evt.Data}");
    }
    
    public void Cleanup()
    {
        // Unsubscribe when no longer needed
        _eventAggregator.Unsubscribe<DataLoadedEvent>(_dataLoadedToken);
    }
}

// Publish an event
public class DataLoader
{
    private readonly IEventAggregator _eventAggregator;
    
    public DataLoader(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
    }
    
    public void LoadData()
    {
        // Load data...
        var data = new { Name = "Example", Value = 42 };
        
        // Notify subscribers
        _eventAggregator.Publish(new DataLoadedEvent(data));
    }
}
```

### SubscriptionToken

**Namespace**: `RimSharp.Core.Commands.Aggregators`

**Description**: Token that represents a subscription to an event.

**Constructors**:

```csharp
SubscriptionToken(Type eventType)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| eventType | Type | The type of event the token is for. |

**Properties**:

| Property | Type | Description |
|----------|------|-------------|
| Token | Guid | Gets the token's unique identifier. |
| EventType | Type | Gets the type of event the token is for. |

**Usage Example**:
```csharp
// Store the token when subscribing
SubscriptionToken token = eventAggregator.Subscribe<MyEvent>(OnMyEvent);

// Use the token to unsubscribe later
eventAggregator.Unsubscribe<MyEvent>(token);

// Inspect token properties if needed
Console.WriteLine($"Token ID: {token.Token}");
Console.WriteLine($"Event Type: {token.EventType.Name}");
```

### WeakEventAggregator

**Namespace**: `RimSharp.Core.Commands.Aggregators`

**Description**: An implementation of IEventAggregator that uses weak references to prevent memory leaks.

**Constructors**:

```csharp
WeakEventAggregator()
```

**Methods**:

| Method | Description | Parameters | Returns |
|--------|-------------|------------|---------|
| Publish&lt;TEvent&gt; | Publishes an event to all subscribers. | TEvent eventToPublish | void |
| Subscribe&lt;TEvent&gt; | Subscribes to an event using a weak reference to the action. | Action&lt;TEvent&gt; action | SubscriptionToken |
| Unsubscribe&lt;TEvent&gt; | Unsubscribes from an event. | SubscriptionToken token | void |
| Purge | Removes all dead references from the subscribers dictionary. | None | void |

**Implementation Example**:
```csharp
// Registration in a DI container
public void ConfigureServices(IServiceCollection services)
{
    // Register the event aggregator as a singleton
    services.AddSingleton<IEventAggregator, WeakEventAggregator>();
}

// Lifecycle management
public class AppLifecycleManager
{
    private readonly WeakEventAggregator _eventAggregator;
    
    public AppLifecycleManager(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator as WeakEventAggregator;
    }
    
    public void PerformMaintenance()
    {
        // Periodically purge dead references
        _eventAggregator?.Purge();
    }
}
```

## Implementation Examples

### Basic Command Usage

The following example demonstrates how to use DelegateCommand in a ViewModel:

```csharp
using System.ComponentModel;
using System.Windows.Input;
using RimSharp.Core.Commands.Base;

public class ContactViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    private string _name;
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    private string _email;
    public string Email
    {
        get => _email;
        set
        {
            _email = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Email)));
        }
    }

    private DelegateCommand _saveContactCommand;
    public ICommand SaveContactCommand => _saveContactCommand ??= new DelegateCommand(
        execute: SaveContact,
        canExecute: CanSaveContact
    ).ObservesProperty(this, nameof(Name))
     .ObservesProperty(this, nameof(Email));

    private bool CanSaveContact()
    {
        return !string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(Email) && Email.Contains("@");
    }

    private void SaveContact()
    {
        // Implementation to save contact data
        Console.WriteLine($"Saving contact: {Name}, {Email}");
    }
}
```

You can bind this command in XAML:

```xml
<Button Content="Save Contact" 
        Command="{Binding SaveContactCommand}" 
        Margin="5" />
```

### Async Command Usage

This example shows how to use AsyncDelegateCommand for operations that require asynchronous execution:

```csharp
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.Core.Commands.Base;

public class DataViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
        }
    }

    private string _searchTerm;
    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            _searchTerm = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchTerm)));
        }
    }

    private AsyncDelegateCommand _searchCommand;
    public ICommand SearchCommand => _searchCommand ??= new AsyncDelegateCommand(
        execute: async () => {
            IsBusy = true;
            try
            {
                await SearchDataAsync();
            }
            finally
            {
                IsBusy = false;
            }
        },
        canExecute: () => !IsBusy && !string.IsNullOrEmpty(SearchTerm)
    ).ObservesProperty(this, nameof(IsBusy))
     .ObservesProperty(this, nameof(SearchTerm));

    private async Task SearchDataAsync()
    {
        // Simulate network operation
        await Task.Delay(2000);
        
        // Perform search with SearchTerm
        Console.WriteLine($"Search completed for: {SearchTerm}");
    }
}
```

### Composite Command Usage

This example demonstrates how to use CompositeCommand to combine multiple commands:

```csharp
using System.Windows.Input;
using RimSharp.Core.Commands.Base;
using RimSharp.Core.Commands.Composite;

public class DocumentViewModel
{
    // Individual commands
    private DelegateCommand _saveCommand;
    public ICommand SaveCommand => _saveCommand ??= new DelegateCommand(
        execute: () => { Console.WriteLine("Saving document..."); }
    );

    private DelegateCommand _validateCommand;
    public ICommand ValidateCommand => _validateCommand ??= new DelegateCommand(
        execute: () => { Console.WriteLine("Validating document..."); }
    );

    private DelegateCommand _backupCommand;
    public ICommand BackupCommand => _backupCommand ??= new DelegateCommand(
        execute: () => { Console.WriteLine("Creating backup..."); }
    );

    // Composite command
    private CompositeCommand _saveAllCommand;
    public ICommand SaveAllCommand => _saveAllCommand ??= CreateSaveAllCommand();

    private CompositeCommand CreateSaveAllCommand()
    {
        var composite = new CompositeCommand();
        composite.RegisterCommand(ValidateCommand);
        composite.RegisterCommand(SaveCommand);
        composite.RegisterCommand(BackupCommand);
        return composite;
    }
}
```

In XAML, you can bind to both individual commands and the composite command:

```xml
<StackPanel>
    <Button Content="Save" Command="{Binding SaveCommand}" Margin="5" />
    <Button Content="Validate" Command="{Binding ValidateCommand}" Margin="5" />
    <Button Content="Backup" Command="{Binding BackupCommand}" Margin="5" />
    <Button Content="Save All" Command="{Binding SaveAllCommand}" Margin="5" />
</StackPanel>
```

### Event Aggregator Usage

This example shows how to use the Event Aggregator pattern for loosely coupled communication:

```csharp
using RimSharp.Core.Commands.Aggregators;

// Define event classes
public class UserLoggedInEvent
{
    public string Username { get; }
    
    public UserLoggedInEvent(string username)
    {
        Username = username;
    }
}

public class DataUpdatedEvent
{
    public object Data { get; }
    public DateTime Timestamp { get; }
    
    public DataUpdatedEvent(object data)
    {
        Data = data;
        Timestamp = DateTime.Now;
    }
}

// Publisher component
public class AuthenticationService
{
    private readonly IEventAggregator _eventAggregator;
    
    public AuthenticationService(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
    }
    
    public void Login(string username, string password)
    {
        // Authentication logic here...
        bool isAuthenticated = true; // Simplified for example
        
        if (isAuthenticated)
        {
            // Publish event to notify subscribers
            _eventAggregator.Publish(new UserLoggedInEvent(username));
        }
    }
}

// Subscriber component
public class UserDashboardViewModel : IDisposable
{
    private readonly IEventAggregator _eventAggregator;
    private SubscriptionToken _loginToken;
    private SubscriptionToken _dataToken;
    
    public UserDashboardViewModel(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
        
        // Subscribe to events
        _loginToken = _eventAggregator.Subscribe<UserLoggedInEvent>(OnUserLoggedIn);
        _dataToken = _eventAggregator.Subscribe<DataUpdatedEvent>(OnDataUpdated);
    }
    
    private void OnUserLoggedIn(UserLoggedInEvent evt)
    {
        Console.WriteLine($"User logged in: {evt.Username}");
        // Update UI or load user-specific data
    }
    
    private void OnDataUpdated(DataUpdatedEvent evt)
    {
        Console.WriteLine($"Data updated at {evt.Timestamp}");
        // Refresh view with updated data
    }
    
    public void Dispose()
    {
        // Unsubscribe to prevent memory leaks
        _eventAggregator.Unsubscribe<UserLoggedInEvent>(_loginToken);
        _eventAggregator.Unsubscribe<DataUpdatedEvent>(_dataToken);
    }
}
```

### Command Service Usage

This example demonstrates how to use the ModCommandService to centralize command management:

```csharp
using System.Windows.Input;
using RimSharp.Core.Commands.Base;
using RimSharp.Core.Commands.Composite;
using RimSharp.Core.Services.Commanding;

// Command initializer implementation
public class ApplicationCommandInitializer : ICommandInitializer
{
    public void Initialize(IModCommandService commandService)
    {
        // Register global application commands
        commandService.RegisterCommand("NewDocument", new DelegateCommand(
            execute: () => { Console.WriteLine("Creating new document..."); }
        ));
        
        commandService.RegisterCommand("OpenDocument", new DelegateCommand(
            execute: () => { Console.WriteLine("Opening document..."); }
        ));
        
        commandService.RegisterCommand("SaveDocument", new DelegateCommand(
            execute: () => { Console.WriteLine("Saving document..."); }
        ));
        
        commandService.RegisterCommand("CloseDocument", new DelegateCommand(
            execute: () => { Console.WriteLine("Closing document..."); }
        ));
        
        // Register composite commands
        var fileOperationsComposite = new CompositeCommand();
        commandService.RegisterCompositeCommand("FileOperations", fileOperationsComposite);
        
        // Add commands to composite
        commandService.AddToCompositeCommand("FileOperations", "NewDocument");
        commandService.AddToCompositeCommand("FileOperations", "OpenDocument");
        commandService.AddToCompositeCommand("FileOperations", "SaveDocument");
        commandService.AddToCompositeCommand("FileOperations", "CloseDocument");
    }
}

// Application startup
public class App
{
    private readonly IModCommandService _commandService;
    private readonly IEnumerable<ICommandInitializer> _commandInitializers;
    
    public App(IModCommandService commandService, IEnumerable<ICommandInitializer> commandInitializers)
    {
        _commandService = commandService;
        _commandInitializers = commandInitializers;
    }
    
    public void Initialize()
    {
        // Initialize all command initializers
        foreach (var initializer in _commandInitializers)
        {
            initializer.Initialize(_commandService);
        }
    }
}

// ViewModel using the command service
public class MainViewModel
{
    private readonly IModCommandService _commandService;
    
    public MainViewModel(IModCommandService commandService)
    {
        _commandService = commandService;
    }
    
    // Expose commands from the service
    public ICommand NewCommand => _commandService.GetCommand("NewDocument");
    public ICommand OpenCommand => _commandService.GetCommand("OpenDocument");
    public ICommand SaveCommand => _commandService.GetCommand("SaveDocument");
    public ICommand CloseCommand => _commandService.GetCommand("CloseDocument");
    public ICommand FileOperationsCommand => _commandService.GetCompositeCommand("FileOperations");
}
```

In XAML, you can bind to these commands:

```xml
<Menu>
    <MenuItem Header="File">
        <MenuItem Header="New" Command="{Binding NewCommand}" />
        <MenuItem Header="Open" Command="{Binding OpenCommand}" />
        <MenuItem Header="Save" Command="{Binding SaveCommand}" />
        <MenuItem Header="Close" Command="{Binding CloseCommand}" />
    </MenuItem>
    <MenuItem Header="Run All File Operations" Command="{Binding FileOperationsCommand}" />
</Menu>
```

### Advanced Command Patterns

Here's an example showing more complex command patterns including parameter usage and command chaining:

```csharp
using System.ComponentModel;
using System.Windows.Input;
using RimSharp.Core.Commands.Base;

public class OrderProcessingViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    
    private readonly IOrderService _orderService;
    private readonly IEventAggregator _eventAggregator;
    
    public OrderProcessingViewModel(IOrderService orderService, IEventAggregator eventAggregator)
    {
        _orderService = orderService;
        _eventAggregator = eventAggregator;
    }
    
    private int _orderId;
    public int OrderId
    {
        get => _orderId;
        set
        {
            _orderId = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OrderId)));
        }
    }
    
    private bool _isProcessing;
    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {
            _isProcessing = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsProcessing)));
        }
    }
    
    // Command with typed parameter
    private DelegateCommand<int> _selectOrderCommand;
    public ICommand SelectOrderCommand => _selectOrderCommand ??= new DelegateCommand<int>(
        execute: (orderId) => {
            OrderId = orderId;
            Console.WriteLine($"Selected order: {orderId}");
        }
    );
    
    // Async command with property observation
    private AsyncDelegateCommand _processOrderCommand;
    public ICommand ProcessOrderCommand => _processOrderCommand ??= new AsyncDelegateCommand(
        execute: async () => {
            IsProcessing = true;
            try
            {
                await _orderService.ProcessOrderAsync(OrderId);
                
                // Publish event after successful processing
                _eventAggregator.Publish(new OrderProcessedEvent(OrderId));
                
                // Chain additional commands
                if (_afterProcessCommand.CanExecute(null))
                {
                    _afterProcessCommand.Execute(null);
                }
            }
            finally
            {
                IsProcessing = false;
            }
        },
        canExecute: () => !IsProcessing && OrderId > 0
    ).ObservesProperty(this, nameof(IsProcessing))
     .ObservesProperty(this, nameof(OrderId));
    
    // Private command for internal use in command chain
    private DelegateCommand _afterProcessCommand = new DelegateCommand(
        execute: () => {
            Console.WriteLine("Performing post-processing tasks...");
        }
    );
}

// Event class for notification
public class OrderProcessedEvent
{
    public int OrderId { get; }
    
    public OrderProcessedEvent(int orderId)
    {
        OrderId = orderId;
    }
}
```

### Multi-Threading and UI Update Example

This example shows how to handle multi-threading concerns with async commands:

```csharp
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.Core.Commands.Base;

public class DataProcessingViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    
    private int _progress;
    public int Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
        }
    }
    
    private bool _isProcessing;
    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {
            _isProcessing = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsProcessing)));
        }
    }
    
    private string _status;
    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }
    
    // Command that supports cancellation
    private CancellationTokenSource _cts;
    private AsyncRelayCommand _processDataCommand;
    public ICommand ProcessDataCommand => _processDataCommand ??= new AsyncRelayCommand(
        execute: async (cancellationToken) => {
            IsProcessing = true;
            Progress = 0;
            Status = "Starting processing...";
            
            try
            {
                // Simulate long-running operation with progress updates
                for (int i = 0; i < 100; i++)
                {
                    // Check cancellation
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    // Update progress
                    Progress = i + 1;
                    Status = $"Processing: {Progress}%";
                    
                    // Simulate work
                    await Task.Delay(100, cancellationToken);
                }
                
                Status = "Processing complete";
            }
            catch (OperationCanceledException)
            {
                Status = "Processing cancelled";
            }
            finally
            {
                IsProcessing = false;
            }
        },
        canExecute: () => !IsProcessing
    ).ObservesProperty(this, nameof(IsProcessing));
    
    // Cancel command
    private DelegateCommand _cancelCommand;
    public ICommand CancelCommand => _cancelCommand ??= new DelegateCommand(
        execute: () => {
            _cts?.Cancel();
            Status = "Cancelling...";
        },
        canExecute: () => IsProcessing
    ).ObservesProperty(this, nameof(IsProcessing));
    
    // Command execution with cancellation support
    public void ExecuteProcessDataCommand()
    {
        // Dispose any existing CTS
        _cts?.Dispose();
        
        // Create new CTS for this execution
        _cts = new CancellationTokenSource();
        
        // Execute the command with the token
        if (_processDataCommand.CanExecute(null))
        {
            _processDataCommand.Execute(_cts.Token);
        }
    }
}
```

In XAML:

```xml
<StackPanel>
    <Button Content="Start Processing" 
            Command="{Binding ProcessDataCommand}" 
            Click="Button_Click" 
            IsEnabled="{Binding !IsProcessing}" />
    <Button Content="Cancel" 
            Command="{Binding CancelCommand}" 
            IsEnabled="{Binding IsProcessing}" />
    <ProgressBar Value="{Binding Progress}" Maximum="100" Height="20" Margin="5" />
    <TextBlock Text="{Binding Status}" Margin="5" />
</StackPanel>
```

Code-behind for the Button_Click event:

```csharp
private void Button_Click(object sender, RoutedEventArgs e)
{
    var viewModel = DataContext as DataProcessingViewModel;
    viewModel?.ExecuteProcessDataCommand();
}
```

This concludes the Implementation Examples section of the RimSharp Command System Technical Documentation.