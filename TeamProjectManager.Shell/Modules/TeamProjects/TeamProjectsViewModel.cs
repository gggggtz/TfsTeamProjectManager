﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;
using System.Windows;
using Microsoft.Practices.Prism.Events;
using Microsoft.TeamFoundation;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Server;
using TeamProjectManager.Common;
using TeamProjectManager.Common.Events;
using TeamProjectManager.Common.Infrastructure;
using TeamProjectManager.Common.ObjectModel;
using TeamProjectManager.Shell.Infrastructure;

namespace TeamProjectManager.Shell.Modules.TeamProjects
{
    [Export]
    public class TeamProjectsViewModel : ViewModelBase
    {
        #region Properties

        public RelayCommand AddTeamProjectCollectionCommand { get; private set; }

        #endregion

        #region Observable Properties

        public IEnumerable<TeamProjectCollectionInfo> TfsTeamProjectCollections
        {
            get { return this.GetValue(TfsTeamProjectCollectionsProperty); }
            set { this.SetValue(TfsTeamProjectCollectionsProperty, value); }
        }

        public static ObservableProperty<IEnumerable<TeamProjectCollectionInfo>> TfsTeamProjectCollectionsProperty = new ObservableProperty<IEnumerable<TeamProjectCollectionInfo>, TeamProjectsViewModel>(o => o.TfsTeamProjectCollections);

        [Export]
        public TeamProjectCollectionInfo SelectedTfsTeamProjectCollection
        {
            get { return this.GetValue(SelectedTfsTeamProjectCollectionProperty); }
            set { this.SetValue(SelectedTfsTeamProjectCollectionProperty, value); }
        }

        public static ObservableProperty<TeamProjectCollectionInfo> SelectedTfsTeamProjectCollectionProperty = new ObservableProperty<TeamProjectCollectionInfo, TeamProjectsViewModel>(o => o.SelectedTfsTeamProjectCollection, null, OnSelectedTfsTeamProjectCollectionChanged);

        [Export]
        public IEnumerable<TeamProjectInfo> TfsTeamProjects
        {
            get { return this.GetValue(TfsTeamProjectsProperty); }
            set { this.SetValue(TfsTeamProjectsProperty, value); }
        }

        public static ObservableProperty<IEnumerable<TeamProjectInfo>> TfsTeamProjectsProperty = new ObservableProperty<IEnumerable<TeamProjectInfo>, TeamProjectsViewModel>(o => o.TfsTeamProjects, OnTfsTeamProjectsChanged);

        public ICollection<TeamProjectInfo> SelectedTfsTeamProjects
        {
            get { return this.GetValue(SelectedTfsTeamProjectsProperty); }
            set { this.SetValue(SelectedTfsTeamProjectsProperty, value); }
        }

        public static ObservableProperty<ICollection<TeamProjectInfo>> SelectedTfsTeamProjectsProperty = new ObservableProperty<ICollection<TeamProjectInfo>, TeamProjectsViewModel>(o => o.SelectedTfsTeamProjects, null, OnSelectedTfsTeamProjectsChanged);

        public Visibility TeamProjectsVisibility
        {
            get { return this.GetValue(TeamProjectsVisibilityProperty); }
            set { this.SetValue(TeamProjectsVisibilityProperty, value); }
        }

        public static ObservableProperty<Visibility> TeamProjectsVisibilityProperty = new ObservableProperty<Visibility, TeamProjectsViewModel>(o => o.TeamProjectsVisibility, Visibility.Collapsed);

        public string InfoMessage
        {
            get { return this.GetValue(InfoMessageProperty); }
            set { this.SetValue(InfoMessageProperty, value); }
        }

        public static ObservableProperty<string> InfoMessageProperty = new ObservableProperty<string, TeamProjectsViewModel>(o => o.InfoMessage);

        public bool IsTeamProjectsLoadComplete
        {
            get { return this.GetValue(IsTeamProjectsLoadCompleteProperty); }
            set { this.SetValue(IsTeamProjectsLoadCompleteProperty, value); }
        }

        public static ObservableProperty<bool> IsTeamProjectsLoadCompleteProperty = new ObservableProperty<bool, TeamProjectsViewModel>(o => o.IsTeamProjectsLoadComplete, true);

        #endregion

        #region Constructors

        [ImportingConstructor]
        public TeamProjectsViewModel(IEventAggregator eventAggregator, ILogger logger)
            : base("Team Projects", eventAggregator, logger)
        {
            this.AddTeamProjectCollectionCommand = new RelayCommand(AddTeamProjectCollection);
            RefreshTeamProjectCollections(null);
        }

        #endregion

        #region Property Change Handlers

        private static void OnTfsTeamProjectsChanged(ObservableObject sender, ObservablePropertyChangedEventArgs<IEnumerable<TeamProjectInfo>> e)
        {
            var viewModel = (TeamProjectsViewModel)sender;
            viewModel.TeamProjectsVisibility = viewModel.TfsTeamProjects == null ? Visibility.Collapsed : Visibility.Visible;
            viewModel.SelectedTfsTeamProjects = null;
        }

        private static void OnSelectedTfsTeamProjectCollectionChanged(ObservableObject sender, ObservablePropertyChangedEventArgs<TeamProjectCollectionInfo> e)
        {
            var viewModel = (TeamProjectsViewModel)sender;
            viewModel.TfsTeamProjects = null;
            if (viewModel.SelectedTfsTeamProjectCollection != null)
            {
                var task = new ApplicationTask(string.Format(CultureInfo.CurrentCulture, "Retrieving team projects for \"{0}\"", viewModel.SelectedTfsTeamProjectCollection.Name));
                viewModel.InfoMessage = "Loading...";
                viewModel.PublishStatus(new StatusEventArgs(task));
                viewModel.IsTeamProjectsLoadComplete = false;
                var worker = new BackgroundWorker();
                worker.DoWork += (bsender, be) =>
                {
                    using (var tfs = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(viewModel.SelectedTfsTeamProjectCollection.Uri))
                    {
                        if (viewModel.SelectedTfsTeamProjectCollection.TeamFoundationServerInfo == null)
                        {
                            viewModel.SelectedTfsTeamProjectCollection.TeamFoundationServerInfo = new TeamFoundationServerInfo(viewModel.GetTfsMajorVersion(tfs));
                        }
                        var store = tfs.GetService<ICommonStructureService>();
                        be.Result = store.ListAllProjects().Where(p => p.Status == ProjectState.WellFormed).Select(p => new TeamProjectInfo(viewModel.SelectedTfsTeamProjectCollection, p.Name, new Uri(p.Uri))).OrderBy(p => p.Name);
                    }
                };
                worker.RunWorkerCompleted += (bsender, be) =>
                {
                    if (be.Error != null)
                    {
                        viewModel.Logger.Log("An unexpected exception occurred while retrieving team projects", be.Error);
                        task.SetError(be.Error);
                        task.SetComplete("An unexpected exception occurred");
                    }
                    else
                    {
                        viewModel.TfsTeamProjects = (IEnumerable<TeamProjectInfo>)be.Result;
                        task.SetComplete("Retrieved " + viewModel.TfsTeamProjects.Count().ToCountString("team project"));
                    }
                    viewModel.InfoMessage = "Connected to " + viewModel.SelectedTfsTeamProjectCollection.TeamFoundationServerInfo.ShortDisplayVersion;
                    viewModel.IsTeamProjectsLoadComplete = true;
                };
                worker.RunWorkerAsync();
            }
        }

        private static void OnSelectedTfsTeamProjectsChanged(ObservableObject sender, ObservablePropertyChangedEventArgs<ICollection<TeamProjectInfo>> e)
        {
            var viewModel = (TeamProjectsViewModel)sender;
            viewModel.EventAggregator.GetEvent<TeamProjectSelectionChangedEvent>().Publish(new TeamProjectSelectionChangedEventArgs(viewModel.SelectedTfsTeamProjectCollection, viewModel.SelectedTfsTeamProjects));
        }

        #endregion

        #region Commands

        private void AddTeamProjectCollection(object argument)
        {
            using (var dialog = new TeamProjectPicker(TeamProjectPickerMode.NoProject, false))
            {
                var result = dialog.ShowDialog(Application.Current.MainWindow.GetIWin32Window());
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    var projectCollection = dialog.SelectedTeamProjectCollection;
                    RegisteredTfsConnections.RegisterProjectCollection(projectCollection);
                    RefreshTeamProjectCollections(projectCollection.Name);
                }
            }
        }

        #endregion

        #region Helper Methods

        private void RefreshTeamProjectCollections(string selectedTeamProjectCollectionName)
        {
            try
            {
                this.InfoMessage = "Loading...";
                this.TfsTeamProjectCollections = RegisteredTfsConnections.GetProjectCollections().Select(c => new TeamProjectCollectionInfo(c.Name, c.Uri));
                this.InfoMessage = "Please select a project collection";
                this.SelectedTfsTeamProjectCollection = this.TfsTeamProjectCollections.FirstOrDefault(t => t.Name == selectedTeamProjectCollectionName);
            }
            catch (Exception exc)
            {
                Logger.Log("An unexpected exception occurred while retrieving the Team Project Collections", exc);
            }
        }

        private TfsMajorVersion GetTfsMajorVersion(TfsTeamProjectCollection tfs)
        {
            try
            {
                var registrationService = (IRegistration)tfs.GetService(typeof(IRegistration));
                var frameworkEntries = registrationService.GetRegistrationEntries("Framework");
                if (frameworkEntries.Length > 0)
                {
                    // We are talking to TFS 2010, which is v10.
                    return TfsMajorVersion.Tfs2010;
                }
                else
                {
                    var vstfsEntries = registrationService.GetRegistrationEntries("vstfs");
                    if (vstfsEntries.Length != 1)
                    {
                        // We must be talking to an unknown version of TFS.
                        return TfsMajorVersion.Unknown;
                    }
                    else
                    {
                        var groupSecurity2Found = false;
                        foreach (ServiceInterface serviceInterface in vstfsEntries[0].ServiceInterfaces)
                        {
                            if (serviceInterface.Name.Equals("GroupSecurity2", StringComparison.OrdinalIgnoreCase))
                            {
                                groupSecurity2Found = true;
                            }
                        }

                        if (groupSecurity2Found)
                        {
                            // We are talking to TFS 2008, which is v9.
                            return TfsMajorVersion.Tfs2008;
                        }
                        else
                        {
                            // We are talking to TFS 2005, which is v8.
                            return TfsMajorVersion.Tfs2005;
                        }
                    }
                }
            }
            catch (TeamFoundationServerException exc)
            {
                this.Logger.Log("An exception occurred while determining the TFS major version", exc);
                return TfsMajorVersion.Unknown;
            }
        }

        #endregion
    }
}