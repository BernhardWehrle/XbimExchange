﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Xbim.Common.Exceptions;
using Xbim.CobieLiteUk.FilterHelper;
using Xbim.CobieLiteUk;
using XbimExchanger.IfcToCOBieLiteUK;
using XbimExchanger.IfcToCOBieLiteUK.Conversion;
using XbimExchanger.IfcToCOBieExpress.Conversion;
using XbimExchanger.IfcHelpers;
using Xbim.Ifc;

namespace Xbim.CobieLiteUk.Client
{
    // ReSharper disable once InconsistentNaming
    public partial class COBieLiteGeneratorDlg : Form
    {
        /// <summary>
        /// Worker
        /// </summary>
        private ICobieConverter _cobieWorker;

        /// <summary>
        /// Filters for Extracting COBie, Role Filtering
        /// </summary>
        private OutPutFilters _assetfilters = new OutPutFilters(); //empty role filter

        /// <summary>
        /// Refrence models to OutPutFilters mappings
        /// </summary>
        public Dictionary<FileInfo, RoleFilter> MapRefModelsRoles { get; private set; }



        /// <summary>
        /// Config File holding mappings for ifc property extractions
        /// </summary>
        public FileInfo ConfigFile { get; private set; }

        /// <summary>
        /// Mappings for COBie proerties from IFC
        /// </summary>
        private CobiePropertyMapping PropertyMaps { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public COBieLiteGeneratorDlg()
        {
            InitializeComponent();

            // Configure the app to use Esent when appropriate
            IfcStore.ModelProviderFactory.UseHeuristicModelProvider();

            //set default role filters held in FillRolesFilterHolder property list
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            ConfigFile = new FileInfo(Path.Combine(dir.FullName, "COBieAttributesCustom.config"));
            if (!ConfigFile.Exists)
            {
                AppendLog("Creating Config File");
                CreateDefaultAppConfig(ConfigFile);
                ConfigFile.Refresh();
            }
            if (_assetfilters.DefaultsNotSet)
            {
                _assetfilters.FillRolesFilterHolderFromDir(dir);
            }

            PropertyMaps = new CobiePropertyMapping(ConfigFile);
            MapRefModelsRoles = new Dictionary<FileInfo, RoleFilter>();
        }

        /// <summary>
        /// Copy resource help config file to working directory
        /// </summary>
        public void CreateDefaultAppConfig(FileInfo configFile)
        {
            var asss = global::System.Reflection.Assembly.GetAssembly(typeof (IfcToCOBieLiteUkExchanger));
            using (var input = asss.GetManifestResourceStream("XbimExchanger.IfcToCOBieLiteUK.COBieAttributes.config"))
            {
                if (input != null) 
                using (var output = configFile.Create())
                {
                    input.CopyTo(output);
                }
            }
            configFile.Refresh();
        }

        /// <summary>
        /// On Load
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void COBieLiteGenerator_Load(object sender, EventArgs e)
        {
            var sysList =
                Enum.GetValues(typeof (SystemExtractionMode))
                    .OfType<SystemExtractionMode>()
                    .Where(r => r != SystemExtractionMode.System);

            checkedListSys.Items.AddRange(sysList.OfType<object>().ToArray());
            for (var i = 0; i < checkedListSys.Items.Count; i++) //set all to ticked
            {
                checkedListSys.SetItemChecked(i, true);
            }
            //set template from resources
            var t1 = Templates.GetAvalilableTemplateTypes();
            var tmps = t1.OfType<object>().ToArray();
            txtTemplate.Items.AddRange(tmps);
            if (txtTemplate.Items.Count > 0)
                txtTemplate.SelectedIndex = 0;
            

            cmBoxCOBieType.DataSource = Enum.GetValues(typeof(COBieType));
            cmboxFiletype.DataSource = Enum.GetValues(typeof(ExportFormatEnum));
            cmboxFiletype.SelectedIndex = 1;

            //set up tooltips
            var tooltip = new ToolTip
            {
                AutoPopDelay = 8000,
                InitialDelay = 1000,
                ReshowDelay = 500,
                ShowAlways = true,
                IsBalloon = true
            };
            tooltip.SetToolTip(chkBoxFlipFilter,
                "Export all excludes to excel file, Note PropertySet Excludes are not flipped");
            tooltip.SetToolTip(chkBoxOpenFile, "Open in excel once file is created");
            tooltip.SetToolTip(rolesList, "Select roles for export filtering");
            tooltip.SetToolTip(btnGenerate, "Generate COBie excel workbook");
            tooltip.SetToolTip(btnBrowse, "Select file to extract COBie from");
            tooltip.SetToolTip(btnFederate, "Create federated file to extract COBie from");
            tooltip.SetToolTip(btnBrowseTemplate, "Select template excel file");
            tooltip.SetToolTip(cmboxFiletype, "Select excel file extension to generate");
            tooltip.SetToolTip(btnClassFilter, "Edit filter configuration for the different role settings");
            tooltip.SetToolTip(btnMergeFilter, "Filter resulting from the merge of all currently selected roles");
            tooltip.SetToolTip(btnPropMaps, "Set PropertySet.PropertyName mappings to be used for conversion to IFC format.");
            tooltip.SetToolTip(chkBoxIds, "Tick to use EntityLabel instead of GUID for identity purposes.");
            tooltip.SetToolTip(checkedListSys,
                "Tick to include additional Systems (ifcSystems always included)\nPropertyMaps = PropertySet Mappings, System Tab in \"Property Maps\" dialog\nTypes = Object Types listing associated components (not assigned to system)");
        }

        /// <summary>
        /// Set roles for this run
        /// </summary>
        private RoleFilter SetRoles()
        {
            var roles = rolesList.Roles;
            if (!chkBoxNoFilter.Checked)
            {
                if (MapRefModelsRoles.Count > 0)
                {
                    foreach (var item in MapRefModelsRoles)
                    {
                        AppendLog(string.Format("{0} has Roles: {1}", item.Key.Name, item.Value.ToString("F")));
                    }
                }
                else
                {
                    AppendLog("Selected Roles: " + roles.ToString("F"));
                }
            }
            else
            {
                AppendLog("Selected Roles: Disabled by no filter flag ");
            }
            return roles;
        }

        /// <summary>
        /// Set the System extraction Methods
        /// </summary>
        /// <returns></returns>
        private SystemExtractionMode SetSystemMode()
        {
            var sysMode = SystemExtractionMode.System;
            var checkedSysMode = checkedListSys.CheckedItems;
            //add the checked system modes
            foreach (var item in checkedSysMode)
            {
                try
                {
                    var mode = (SystemExtractionMode)Enum.Parse(typeof(SystemExtractionMode), item.ToString());    
                    sysMode |= mode;
                }
                catch (Exception)
                {
                    AppendLog("Error: Failed to get requested system extraction mode");
                }
            }

            return sysMode;
        }

        /// <summary>
        /// On Click Generate Button
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnGenerate_Click(object sender, EventArgs e)
        {
            if (chkBoxFlipFilter.Checked)
            {
                // ReSharper disable LocalizableElement
                var result = MessageBox.Show(
                    "Flip Filter is ticked, this will show only excluded items, Do you want to continue",
                    "Warning", MessageBoxButtons.YesNo);
                if (result == DialogResult.No)
                {
                    return;
                }
            }
            btnGenerate.Enabled = false;

            //get required type of COBie
            COBieType cobieType;
            Enum.TryParse<COBieType>(cmBoxCOBieType.SelectedValue.ToString(), out cobieType);

            if (cobieType == COBieType.COBieLiteUK)
            {
                if (_cobieWorker == null || !(_cobieWorker is CobieLiteConverter))
                {
                    _cobieWorker = new CobieLiteConverter();
                    _cobieWorker.Worker.ProgressChanged += WorkerProgressChanged;
                    _cobieWorker.Worker.RunWorkerCompleted += WorkerCompleted;
                } 
            }
            else if (cobieType == COBieType.COBieExpress)
            {
                if (_cobieWorker == null || !(_cobieWorker is CobieExpressConverter))
                {
                    _cobieWorker = new CobieExpressConverter();
                    _cobieWorker.Worker.ProgressChanged += WorkerProgressChanged;
                    _cobieWorker.Worker.RunWorkerCompleted += WorkerCompleted;
                }
            }
            else
            {
                throw new ArgumentException("Unknown COBieType requested");
            }
            //get Excel File Type
            var excelType = GetExcelType();
            //set filters
            var filterRoles = SetRoles();
            if (!chkBoxNoFilter.Checked)
            {
                _assetfilters.ApplyRoleFilters(filterRoles);
                _assetfilters.FlipResult = chkBoxFlipFilter.Checked;
            }

            //set parameters
            var conversionSettings = new CobieConversionParams
            {
                Source =  txtPath.Text,
                OutputFileName = Path.ChangeExtension(txtPath.Text, "Cobie"),
                TemplateFile = txtTemplate.Text,
                ExportFormat = excelType,
                ExtId = chkBoxIds.Checked ? EntityIdentifierMode.IfcEntityLabels : EntityIdentifierMode.GloballyUniqueIds,
                SysMode = SetSystemMode(),
                Filter = chkBoxNoFilter.Checked ? new OutPutFilters() : _assetfilters,
                ConfigFile = ConfigFile.FullName,
                Log = chkBoxLog.Checked
            };
            
            //run worker
            _cobieWorker.Run(conversionSettings);

        }

        #region Worker Methods

        /// <summary>
        /// Worker Complete method
        /// </summary>
        /// <param name="s"></param>
        /// <param name="args"></param>
        public void WorkerCompleted(object s, RunWorkerCompletedEventArgs args)
        {
            try
            {
                var ex = args.Result as Exception;
                if (ex != null)
                {
                    var sb = new StringBuilder();

                    var indent = "";
                    while (ex != null)
                    {
                        sb.AppendFormat("{0}{1}\n", indent, ex.Message);
                        ex = ex.InnerException;
                        indent += "\t";
                    }
                    AppendLog(sb.ToString());
                }
                else
                {
                    var errMsg = args.Result as string;
                    if (!string.IsNullOrEmpty(errMsg))
                        AppendLog(errMsg);

                }
            }
            catch (Exception ex)
            {
                var sb = new StringBuilder();
                var indent = "";

                while (ex != null)
                {
                    sb.AppendFormat("{0}{1}\n", indent, ex.Message);
                    ex = ex.InnerException;
                    indent += "\t";
                }
                AppendLog(sb.ToString());
            }
            finally
            {
                btnGenerate.Enabled = true;
            }
            //open file if ticked to open excel
            if (chkBoxOpenFile.Checked && args.Result is IEnumerable<string>)
            {
                var files = args.Result as IEnumerable<string>;
                foreach (var file in files)
                {
                    if (!string.IsNullOrEmpty(file) && File.Exists(file))
                        try
                        {
                            Process.Start(file);
                        }
                        catch (Exception)
                        {

                            throw new ArgumentException("File extension is not associated with a program");
                        } 
                }
            }
            ProgressBar.Visible = false;
        }

        /// <summary>
        /// Worker Progress Changed
        /// </summary>
        /// <param name="s"></param>
        /// <param name="args"></param>
        public void WorkerProgressChanged(object s, ProgressChangedEventArgs args)
        {

            //Show message in Text List Box
            if (args.ProgressPercentage == 0)
            {
                StatusMsg.Text = string.Empty;
                ProgressBar.Visible = false;
                AppendLog(args.UserState.ToString());
            }
            else //show message on status bar and update progress bar
            {
                if (ProgressBar.Visible == false)
                {
                    ProgressBar.Visible = true;
                }
                StatusMsg.Text = args.UserState.ToString();
                ProgressBar.Value = args.ProgressPercentage;
            }
        }

        /// <summary>
        /// Add string to Text Output List Box 
        /// </summary>
        /// <param name="text"></param>
        private void AppendLog(string text)
        {
            txtOutput.AppendText(text + Environment.NewLine);
            txtOutput.ScrollToCaret();
        }

        #endregion


        /// <summary>
        /// Get Excel Type From Combo
        /// </summary>
        /// <returns>ExcelTypeEnum</returns>
        private ExportFormatEnum GetExcelType()
        {
            return (ExportFormatEnum) Enum.Parse(typeof (ExportFormatEnum), cmboxFiletype.Text);
        }


        /// <summary>
        /// On Click Browse Model File Button
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnBrowse_Click(object sender, EventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter =
                    "All XBim Files|*.ifc;*.ifcxml;*.ifczip;*.xbim;*.xbimf|IFC Files|*.ifc;*.ifcxml;*.ifczip|Xbim Files|*.xbim|Xbim Federated Files|*.xbimf|Excel Files|*.xls;*.xlsx|Json Files|*.json|Xml Files|*.xml",
                Title = "Choose a source file",
                CheckFileExists = true
            };

            // Show open file dialog box 
            if (dlg.ShowDialog() != DialogResult.OK)
                return;


            txtPath.Text = dlg.FileName;
            UpdateUiFromFilename();
        }

        private void UpdateUiFromFilename()
        {
            UpdateCheckOpenExcel();
            var fileExt = Path.GetExtension(txtPath.Text);
            MapRefModelsRoles.Clear();
            if (string.IsNullOrEmpty(fileExt))
                return;
            if (fileExt.Equals(".xbimf", StringComparison.OrdinalIgnoreCase))
            {
                // this gets the federated model from an xbimf file
                using (var fedModel = new FederatedModel(new FileInfo(txtPath.Text)))
                {
                    if (fedModel.Model.IsFederation)
                    {
                        MapRefModelsRoles = fedModel.RefModelRoles.ToDictionary(m => new FileInfo(m.Key.Name),
                            m => m.Value);
                    }
                    else
                    {
                        throw new XbimException(string.Format("Model is not Federated: {0}", txtPath.Text));
                    }
                }
            }
            rolesList.Enabled = !(
                fileExt.Equals(".xbimf", StringComparison.OrdinalIgnoreCase)
                || fileExt.Equals(".json", StringComparison.OrdinalIgnoreCase)
                || fileExt.Equals(".xml", StringComparison.OrdinalIgnoreCase)
                || fileExt.Equals(".xls", StringComparison.OrdinalIgnoreCase)
                || fileExt.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                );
        }

        private void UpdateCheckOpenExcel()
        {
            chkBoxOpenFile.Enabled = cmboxFiletype.Text.Equals("XLS", StringComparison.OrdinalIgnoreCase) ||
                                     cmboxFiletype.Text.Equals("XLSX", StringComparison.OrdinalIgnoreCase) ||
                                     cmboxFiletype.Text.Equals("STEP21", StringComparison.OrdinalIgnoreCase);
            chkBoxOpenFile.Checked = chkBoxOpenFile.Enabled;
        }

        /// <summary>
        /// On Click federated button
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnFederate_Click(object sender, EventArgs e)
        {
            var fedDlg = new Federate(txtPath.Text);
            var result = fedDlg.ShowDialog(this);
            if (result == DialogResult.OK)
            {
                txtPath.Text = fedDlg.FileName;
                MapRefModelsRoles = fedDlg.FileRoles;
            }
            var ext = Path.GetExtension(txtPath.Text);
            if (string.IsNullOrEmpty(ext))
                return;
            rolesList.Enabled = (ext.ToUpperInvariant() != ".XBIMF");
        }

        /// <summary>
        /// Select template from file system
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnBrowseTemplate_Click(object sender, EventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Choose a COBie template file",
                Filter = "Excel Files|*.xls;*.xlsx",
                CheckFileExists = true
            };

            if (dlg.ShowDialog() != DialogResult.OK) 
                return;
            txtTemplate.Text = dlg.FileName;
            SetExcelType();
            cmboxFiletype.Enabled = false;
        }

        /// <summary>
        /// set the Excel type combo box from template extension
        /// </summary>
        private void SetExcelType()
        {
            var ext = Path.GetExtension(txtTemplate.Text);
            if (string.IsNullOrEmpty(ext)) 
                return;
            ext = ext.Substring(1).ToUpper();
            cmboxFiletype.SelectedIndex = cmboxFiletype.Items.IndexOf(ext);
        }

        /// <summary>
        /// Change on templates combo
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void txtTemplate_SelectedIndexChanged(object sender, EventArgs e)
        {
            cmboxFiletype.Enabled = true; //enable excel file type combo
        }

        /// <summary>
        /// Clear Text Box
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnClear_Click(object sender, EventArgs e)
        {
            txtOutput.Clear();
        }

        /// <summary>
        /// Display notes on filter flip option
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void chkBoxFlipFilter_CheckedChanged(object sender, EventArgs e)
        {
            if (chkBoxFlipFilter.Checked)
            {
                txtOutput.AppendText(
                    "Test purposes Only: Will export filtered items to excel workbook, ignoring property set filters to ensure property names are shown" +
                    Environment.NewLine);
            }
            else
            {
                txtOutput.AppendText("Filter Flip off" + Environment.NewLine);
            }
        }

        /// <summary>
        /// On Click Class filter Button
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnClassFilter_Click(object sender, EventArgs e)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            //set the defaults if not already set
            if (_assetfilters.DefaultsNotSet)
            {
                _assetfilters.FillRolesFilterHolderFromDir(dir);
            }
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                var filterDlg = new FilterDlg(_assetfilters);
                if (filterDlg.ShowDialog() != DialogResult.OK) 
                    return;
                _assetfilters = filterDlg.RolesFilters;
                _assetfilters.WriteXmlRolesFilterHolderToDir(dir);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        /// <summary>
        /// On Click Merge Filter Button
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnMergeFilter_Click(object sender, EventArgs e)
        {
            //set the defaults if not already set
            if (_assetfilters.DefaultsNotSet)
            {
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                _assetfilters.FillRolesFilterHolderFromDir(dir);
            }
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                FilterDlg filterDlg;
                var fileExt = Path.GetExtension(txtPath.Text);

                if (fileExt != null && fileExt.Equals(".xbimf", StringComparison.OrdinalIgnoreCase))
                {
                    var fedFilters = _assetfilters.SetFedModelFilter(MapRefModelsRoles);
                    filterDlg = new FilterDlg(_assetfilters, true, fedFilters);
                }
                else
                {
                    MapRefModelsRoles.Clear();
                    var roles = SetRoles();
                    _assetfilters.ApplyRoleFilters(roles);
                    filterDlg = new FilterDlg(_assetfilters, true);
                }
                filterDlg.ShowDialog();
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        /// <summary>
        /// Check Box Change on No Filter Tick Box
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void chkBoxNoFilter_CheckedChanged(object sender, EventArgs e)
        {
            chkBoxFlipFilter.Enabled = !chkBoxNoFilter.Checked;
            btnClassFilter.Enabled = !chkBoxNoFilter.Checked;
            btnMergeFilter.Enabled = !chkBoxNoFilter.Checked;
            rolesList.Enabled = !chkBoxNoFilter.Checked;
            if (chkBoxNoFilter.Checked)
            {
                txtOutput.AppendText("Filter Off" + Environment.NewLine);
            }
            else
            {
                txtOutput.AppendText("Filter On" + Environment.NewLine);
            }
        }

        /// <summary>
        /// Show the Property Fields Map Dialog
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnPropMaps_Click(object sender, EventArgs e)
        {
            var propMapDlg = new PropertyMapDlg(PropertyMaps);
            var result = propMapDlg.ShowDialog(this);
            if (result != DialogResult.OK) 
                return;
            PropertyMaps = propMapDlg.PropertyMaps;
            PropertyMaps.Save();
        }

        private void cmboxFiletype_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateCheckOpenExcel();

            grpIfcGenerationMaps.Enabled = cmboxFiletype.Text.Equals("IFC", StringComparison.OrdinalIgnoreCase);


        }
    }

    enum COBieType
    {
        COBieLiteUK,
        COBieExpress,

    }

    // ReSharper disable InconsistentNaming
}
