using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;

namespace PluginCodeTest
{
    public abstract partial class BasePlugin : IPlugin
    {
        protected class LocalPluginContext<T> : IDisposable where T : Entity
        {
            internal Microsoft.Xrm.Sdk.Client.OrganizationServiceContext CrmContext { get; private set; }
            internal IServiceProvider ServiceProvider { get; private set; }
            internal IOrganizationServiceFactory ServiceFactory { get; private set; }
            internal IOrganizationService OrganizationService { get; private set; }
            internal IPluginExecutionContext PluginExecutionContext { get; private set; }
            internal ITracingService TracingService { get; private set; }
            internal eStage Stage { get { return (eStage)this.PluginExecutionContext.Stage; } }
            internal int Depth { get { return this.PluginExecutionContext.Depth; } }
            internal string MessageName { get { return this.PluginExecutionContext.MessageName; } }
            internal LocalPluginContext(IServiceProvider serviceProvider)
            {
                if (serviceProvider == null)
                    throw new ArgumentNullException("serviceProvider");

                // Obtain the tracing service from the service provider.
                this.TracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

                // Obtain the execution context service from the service provider.
                this.PluginExecutionContext = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

                // Obtain the Organization Service factory service from the service provider
                this.ServiceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

                // Use the factory to generate the Organization Service.
                this.OrganizationService = this.ServiceFactory.CreateOrganizationService(this.PluginExecutionContext.UserId);

                // Generate the CrmContext to use with LINQ etc
                this.CrmContext = new Microsoft.Xrm.Sdk.Client.OrganizationServiceContext(this.OrganizationService);
            }

            internal void Trace(string message)
            {
                if (string.IsNullOrWhiteSpace(message) || this.TracingService == null) return;

                if (this.PluginExecutionContext == null)
                    this.TracingService.Trace(message);
                else
                {
                    this.TracingService.Trace(
                        "{0}, Correlation Id: {1}, Initiating User: {2}",
                        message,
                        this.PluginExecutionContext.CorrelationId,
                        this.PluginExecutionContext.InitiatingUserId);
                }
            }

            public void Dispose()
            {
                if (this.CrmContext != null)
                    this.CrmContext.Dispose();
            }
            /// <summary>
            /// Returns the first registered 'Pre' image for the pipeline execution
            /// </summary>
            internal T PreImage
            {
                get
                {
                    if (this.PluginExecutionContext.PreEntityImages.Any())
                        return GetEntityAsType(this.PluginExecutionContext.PreEntityImages[this.PluginExecutionContext.PreEntityImages.FirstOrDefault().Key]);
                    return null;
                }
            }
            /// <summary>
            /// Returns the first registered 'Post' image for the pipeline execution
            /// </summary>
            internal T PostImage
            {
                get
                {
                    if (this.PluginExecutionContext.PostEntityImages.Any())
                        return GetEntityAsType(this.PluginExecutionContext.PostEntityImages[this.PluginExecutionContext.PostEntityImages.FirstOrDefault().Key]);
                    return null;
                }
            }
            /// <summary>
            /// Returns the 'Target' of the message if available
            /// This is an 'Entity' instead of the specified type in order to retain the same instance of the 'Entity' object. This allows for updates to the target in a 'Pre' stage that
            /// will get persisted during the transaction.
            /// </summary>
            internal Entity TargetEntity
            {
                get
                {
                    if (this.PluginExecutionContext.InputParameters.Contains("Target"))
                        return this.PluginExecutionContext.InputParameters["Target"] as Entity;
                    return null;
                }
            }
            /// <summary>
            /// Returns the 'Target' of the message as an EntityReference if available
            /// </summary>
            internal EntityReference TargetEntityReference
            {
                get
                {
                    if (this.PluginExecutionContext.InputParameters.Contains("Target"))
                        return this.PluginExecutionContext.InputParameters["Target"] as EntityReference;
                    return null;
                }
            }
            private T GetEntityAsType(Entity entity)
            {
                if (typeof(T) == entity.GetType())
                    return entity as T;
                else
                    return entity.ToEntity<T>();
            }
        }
        protected enum eStage
        {
            PreValidation = 10,
            PreOperation = 20,
            PostOperation = 40
        }
        protected class PluginEvent
        {
            /// <summary>
            /// Execution pipeline stage that the plugin should be registered against.
            /// </summary>
            public eStage Stage { get; set; }
            /// <summary>
            /// Logical name of the entity that the plugin should be registered against. Leave 'null' to register against all entities.
            /// </summary>
            public string EntityName { get; set; }
            /// <summary>
            /// Name of the message that the plugin should be triggered off of.
            /// </summary>
            public string MessageName { get; set; }
            /// <summary>
            /// Method that should be executed when the conditions of the Plugin Event have been met.
            /// </summary>
            public Action<IServiceProvider> PluginAction { get; set; }
        }

        private Collection<PluginEvent> registeredEvents;

        /// <summary>
        /// Gets the List of events that the plug-in should fire for. Each List
        /// </summary>
        protected Collection<PluginEvent> RegisteredEvents
        {
            get
            {
                if (this.registeredEvents == null)
                    this.registeredEvents = new Collection<PluginEvent>();
                return this.registeredEvents;
            }
        }

        /// <summary>
        /// Initializes a new instance of the BasePlugin class.
        /// </summary>
        internal BasePlugin(string unsecureConfig, string secureConfig)
        {
            this.UnsecureConfig = unsecureConfig;
            this.SecureConfig = secureConfig;
        }
        /// <summary>
        /// Un secure configuration specified during the registration of the plugin step
        /// </summary>
        public string UnsecureConfig { get; private set; }

        /// <summary>
        /// Secure configuration specified during the registration of the plugin step
        /// </summary>
        public string SecureConfig { get; private set; }

        /// <summary>
        /// Executes the plug-in.
        /// </summary>
        /// <param name="serviceProvider">The service provider.</param>
        /// <remarks>
        /// For improved performance, Microsoft Dynamics CRM caches plug-in instances. 
        /// The plug-in's Execute method should be written to be stateless as the constructor 
        /// is not called for every invocation of the plug-in. Also, multiple system threads 
        /// could execute the plug-in at the same time. All per invocation state information 
        /// is stored in the context. This means that you should not use global variables in plug-ins.
        /// </remarks>
        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
                throw new ArgumentNullException("serviceProvider");

            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var pluginContext = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

            tracingService.Trace(string.Format(CultureInfo.InvariantCulture, "Entered {0}.Execute()", this.GetType().ToString()));
            try
            {
                // Iterate over all of the expected registered events to ensure that the plugin
                // has been invoked by an expected event
                var entityActions =
                    (from a in this.RegisteredEvents
                     where (
                        (int)a.Stage == pluginContext.Stage &&
                         (string.IsNullOrWhiteSpace(a.MessageName) ? true : a.MessageName.ToLowerInvariant() == pluginContext.MessageName.ToLowerInvariant()) &&
                         (string.IsNullOrWhiteSpace(a.EntityName) ? true : a.EntityName.ToLowerInvariant() == pluginContext.PrimaryEntityName.ToLowerInvariant())
                     )
                     select a.PluginAction);

                if (entityActions.Any())
                {
                    foreach (var entityAction in entityActions)
                    {
                        tracingService.Trace(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} is firing for Entity: {1}, Message: {2}, Method: {3}",
                            this.GetType().ToString(),
                            pluginContext.PrimaryEntityName,
                            pluginContext.MessageName,
                            entityAction.Method.Name));

                        entityAction.Invoke(serviceProvider);
                    }
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace(string.Format(CultureInfo.InvariantCulture, "Exception: {0}", ex.ToString()));
                throw;
            }
            finally
            {
                tracingService.Trace(string.Format(CultureInfo.InvariantCulture, "Exiting {0}.Execute()", this.GetType().ToString()));
            }
        }
    }
    public struct EntityNames
    {
        public static readonly string account = "account";
    }
    public struct MessageNames
    {
        public static readonly string AddChannelAccessProfilePrivileges = "AddChannelAccessProfilePrivileges";
  public static readonly string AddItem = "AddItem";
  public static readonly string AddListMembers = "AddListMembers";
  public static readonly string AddMember = "AddMember";
  public static readonly string AddMembers = "AddMembers";
  public static readonly string AddPrincipalToQueue = "AddPrincipalToQueue";
  public static readonly string AddPrivileges = "AddPrivileges";
  public static readonly string AddProductToKit = "AddProductToKit";
  public static readonly string AddRecurrence = "AddRecurrence";
  public static readonly string AddSolutionComponent = "AddSolutionComponent";
  public static readonly string AddSubstitute = "AddSubstitute";
  public static readonly string AddToQueue = "AddToQueue";
  public static readonly string AddUserToRecordTeam = "AddUserToRecordTeam";
  public static readonly string ApplyRecordCreationAndUpdateRule = "ApplyRecordCreationAndUpdateRule";
  public static readonly string ApplyRoutingRule = "ApplyRoutingRule";
  public static readonly string Assign = "Assign";
  public static readonly string AssignUserRoles = "AssignUserRoles";
  public static readonly string Associate = "Associate";
  public static readonly string AssociateEntities = "AssociateEntities";
  public static readonly string AutoMapEntity = "AutoMapEntity";
  public static readonly string BackgroundSend = "BackgroundSend";
  public static readonly string Book = "Book";
  public static readonly string BulkDelete = "BulkDelete";
  public static readonly string BulkDelete2 = "BulkDelete2";
  public static readonly string BulkDetectDuplicates = "BulkDetectDuplicates";
  public static readonly string BulkMail = "BulkMail";
  public static readonly string CalculateActualValue = "CalculateActualValue";
  public static readonly string CalculatePrice = "CalculatePrice";
  public static readonly string CalculateRollupField = "CalculateRollupField";
  public static readonly string CalculateTotalTime = "CalculateTotalTime";
  public static readonly string CanBeReferenced = "CanBeReferenced";
  public static readonly string CanBeReferencing = "CanBeReferencing";
  public static readonly string Cancel = "Cancel";
  public static readonly string CanManyToMany = "CanManyToMany";
  public static readonly string CheckIncoming = "CheckIncoming";
  public static readonly string CheckPromote = "CheckPromote";
  public static readonly string Clone = "Clone";
  public static readonly string CloneAsPatch = "CloneAsPatch";
  public static readonly string CloneAsSolution = "CloneAsSolution";
  public static readonly string CloneMobileOfflineProfile = "CloneMobileOfflineProfile";
  public static readonly string CloneProduct = "CloneProduct";
  public static readonly string Close = "Close";
  public static readonly string CompoundCreate = "CompoundCreate";
  public static readonly string CompoundUpdate = "CompoundUpdate";
  public static readonly string CompoundUpdateDuplicateDetectionRule = "CompoundUpdateDuplicateDetectionRule";
  public static readonly string ConvertDateAndTimeBehavior = "ConvertDateAndTimeBehavior";
  public static readonly string ConvertKitToProduct = "ConvertKitToProduct";
  public static readonly string ConvertOwnerTeamToAccessTeam = "ConvertOwnerTeamToAccessTeam";
  public static readonly string ConvertProductToKit = "ConvertProductToKit";
  public static readonly string ConvertQuoteToSalesOrder = "ConvertQuoteToSalesOrder";
  public static readonly string ConvertSalesOrderToInvoice = "ConvertSalesOrderToInvoice";
  public static readonly string Copy = "Copy";
  public static readonly string CopyCampaignResponse = "CopyCampaignResponse";
  public static readonly string CopyDynamicListToStatic = "CopyDynamicListToStatic";
  public static readonly string CopyMembers = "CopyMembers";
  public static readonly string CopySystemForm = "CopySystemForm";
  public static readonly string Create = "Create";
  public static readonly string CreateActivities = "CreateActivities";
  public static readonly string CreateAttribute = "CreateAttribute";
  public static readonly string CreateCustomerRelationships = "CreateCustomerRelationships";
  public static readonly string CreateEntity = "CreateEntity";
  public static readonly string CreateEntityKey = "CreateEntityKey";
  public static readonly string CreateException = "CreateException";
  public static readonly string CreateInstance = "CreateInstance";
  public static readonly string CreateKnowledgeArticleTranslation = "CreateKnowledgeArticleTranslation";
  public static readonly string CreateKnowledgeArticleVersion = "CreateKnowledgeArticleVersion";
  public static readonly string CreateManyToMany = "CreateManyToMany";
  public static readonly string CreateOneToMany = "CreateOneToMany";
  public static readonly string CreateOptionSet = "CreateOptionSet";
  public static readonly string CreateWorkflowFromTemplate = "CreateWorkflowFromTemplate";
  public static readonly string Delete = "Delete";
  public static readonly string DeleteAndPromote = "DeleteAndPromote";
  public static readonly string DeleteAttribute = "DeleteAttribute";
  public static readonly string DeleteAuditData = "DeleteAuditData";
  public static readonly string DeleteEntity = "DeleteEntity";
  public static readonly string DeleteEntityKey = "DeleteEntityKey";
  public static readonly string DeleteOpenInstances = "DeleteOpenInstances";
  public static readonly string DeleteOptionSet = "DeleteOptionSet";
  public static readonly string DeleteOptionValue = "DeleteOptionValue";
  public static readonly string DeleteRecordChangeHistory = "DeleteRecordChangeHistory";
  public static readonly string DeleteRelationship = "DeleteRelationship";
  public static readonly string DeliverIncoming = "DeliverIncoming";
  public static readonly string DeliverPromote = "DeliverPromote";
  public static readonly string DeprovisionLanguage = "DeprovisionLanguage";
  public static readonly string DetachFromQueue = "DetachFromQueue";
  public static readonly string Disassociate = "Disassociate";
  public static readonly string DisassociateEntities = "DisassociateEntities";
  public static readonly string DistributeCampaignActivity = "DistributeCampaignActivity";
  public static readonly string DownloadReportDefinition = "DownloadReportDefinition";
  public static readonly string EntityExpressionToFetchXml = "EntityExpressionToFetchXml";
  public static readonly string Execute = "Execute";
  public static readonly string ExecuteAsync = "ExecuteAsync";
  public static readonly string ExecuteById = "ExecuteById";
  public static readonly string ExecuteMultiple = "ExecuteMultiple";
  public static readonly string ExecuteTransaction = "ExecuteTransaction";
  public static readonly string ExecuteWorkflow = "ExecuteWorkflow";
  public static readonly string Expand = "Expand";
  public static readonly string Export = "Export";
  public static readonly string ExportAll = "ExportAll";
  public static readonly string ExportCompressed = "ExportCompressed";
  public static readonly string ExportCompressedAll = "ExportCompressedAll";
  public static readonly string ExportCompressedTranslations = "ExportCompressedTranslations";
  public static readonly string ExportFieldTranslation = "ExportFieldTranslation";
  public static readonly string ExportMappings = "ExportMappings";
  public static readonly string ExportSolution = "ExportSolution";
  public static readonly string ExportTranslation = "ExportTranslation";
  public static readonly string ExportTranslations = "ExportTranslations";
  public static readonly string FetchXmlToEntityExpression = "FetchXmlToEntityExpression";
  public static readonly string FindParent = "FindParent";
  public static readonly string Fulfill = "Fulfill";
  public static readonly string FullTextSearchKnowledgeArticle = "FullTextSearchKnowledgeArticle";
  public static readonly string GenerateInvoiceFromOpportunity = "GenerateInvoiceFromOpportunity";
  public static readonly string GenerateQuoteFromOpportunity = "GenerateQuoteFromOpportunity";
  public static readonly string GenerateSalesOrderFromOpportunity = "GenerateSalesOrderFromOpportunity";
  public static readonly string GenerateSocialProfile = "GenerateSocialProfile";
  public static readonly string GetAllTimeZonesWithDisplayName = "GetAllTimeZonesWithDisplayName";
  public static readonly string GetDecryptionKey = "GetDecryptionKey";
  public static readonly string GetDefaultPriceLevel = "GetDefaultPriceLevel";
  public static readonly string GetDistinctValues = "GetDistinctValues";
  public static readonly string GetHeaderColumns = "GetHeaderColumns";
  public static readonly string GetInvoiceProductsFromOpportunity = "GetInvoiceProductsFromOpportunity";
  public static readonly string GetQuantityDecimal = "GetQuantityDecimal";
  public static readonly string GetQuoteProductsFromOpportunity = "GetQuoteProductsFromOpportunity";
  public static readonly string GetReportHistoryLimit = "GetReportHistoryLimit";
  public static readonly string GetSalesOrderProductsFromOpportunity = "GetSalesOrderProductsFromOpportunity";
  public static readonly string GetTimeZoneCodeByLocalizedName = "GetTimeZoneCodeByLocalizedName";
  public static readonly string GetTrackingToken = "GetTrackingToken";
  public static readonly string GetValidManyToMany = "GetValidManyToMany";
  public static readonly string GetValidReferencedEntities = "GetValidReferencedEntities";
  public static readonly string GetValidReferencingEntities = "GetValidReferencingEntities";
  public static readonly string GrantAccess = "GrantAccess";
  public static readonly string Handle = "Handle";
  public static readonly string Import = "Import";
  public static readonly string ImportAll = "ImportAll";
  public static readonly string ImportCompressedAll = "ImportCompressedAll";
  public static readonly string ImportCompressedTranslationsWithProgress = "ImportCompressedTranslationsWithProgress";
  public static readonly string ImportCompressedWithProgress = "ImportCompressedWithProgress";
  public static readonly string ImportFieldTranslation = "ImportFieldTranslation";
  public static readonly string ImportMappings = "ImportMappings";
  public static readonly string ImportRecords = "ImportRecords";
  public static readonly string ImportSolution = "ImportSolution";
  public static readonly string ImportTranslation = "ImportTranslation";
  public static readonly string ImportTranslationsWithProgress = "ImportTranslationsWithProgress";
  public static readonly string ImportWithProgress = "ImportWithProgress";
  public static readonly string IncrementKnowledgeArticleViewCount = "IncrementKnowledgeArticleViewCount";
  public static readonly string InitializeFrom = "InitializeFrom";
  public static readonly string InsertOptionValue = "InsertOptionValue";
  public static readonly string InsertStatusValue = "InsertStatusValue";
  public static readonly string InstallSampleData = "InstallSampleData";
  public static readonly string Instantiate = "Instantiate";
  public static readonly string InstantiateFilters = "InstantiateFilters";
  public static readonly string IsBackOfficeInstalled = "IsBackOfficeInstalled";
  public static readonly string IsComponentCustomizable = "IsComponentCustomizable";
  public static readonly string IsDataEncryptionActive = "IsDataEncryptionActive";
  public static readonly string IsValidStateTransition = "IsValidStateTransition";
  public static readonly string LocalTimeFromUtcTime = "LocalTimeFromUtcTime";
  public static readonly string LockInvoicePricing = "LockInvoicePricing";
  public static readonly string LockSalesOrderPricing = "LockSalesOrderPricing";
  public static readonly string Lose = "Lose";
  public static readonly string MakeAvailableToOrganization = "MakeAvailableToOrganization";
  public static readonly string MakeUnavailableToOrganization = "MakeUnavailableToOrganization";
  public static readonly string Merge = "Merge";
  public static readonly string ModifyAccess = "ModifyAccess";
  public static readonly string OrderOption = "OrderOption";
  public static readonly string Parse = "Parse";
  public static readonly string PickFromQueue = "PickFromQueue";
  public static readonly string ProcessInbound = "ProcessInbound";
  public static readonly string PropagateByExpression = "PropagateByExpression";
  public static readonly string ProvisionLanguage = "ProvisionLanguage";
  public static readonly string Publish = "Publish";
  public static readonly string PublishAll = "PublishAll";
  public static readonly string PublishProductHierarchy = "PublishProductHierarchy";
  public static readonly string PublishTheme = "PublishTheme";
  public static readonly string QualifyLead = "QualifyLead";
  public static readonly string QualifyMember = "QualifyMember";
  public static readonly string Query = "Query";
  public static readonly string QueryMultiple = "QueryMultiple";
  public static readonly string ReactivateEntityKey = "ReactivateEntityKey";
  public static readonly string ReassignObjects = "ReassignObjects";
  public static readonly string ReassignObjectsEx = "ReassignObjectsEx";
  public static readonly string Recalculate = "Recalculate";
  public static readonly string ReleaseToQueue = "ReleaseToQueue";
  public static readonly string RemoveFromQueue = "RemoveFromQueue";
  public static readonly string RemoveItem = "RemoveItem";
  public static readonly string RemoveMember = "RemoveMember";
  public static readonly string RemoveMembers = "RemoveMembers";
  public static readonly string RemoveParent = "RemoveParent";
  public static readonly string RemovePrivilege = "RemovePrivilege";
  public static readonly string RemoveProductFromKit = "RemoveProductFromKit";
  public static readonly string RemoveRelated = "RemoveRelated";
  public static readonly string RemoveSolutionComponent = "RemoveSolutionComponent";
  public static readonly string RemoveSubstitute = "RemoveSubstitute";
  public static readonly string RemoveUserFromRecordTeam = "RemoveUserFromRecordTeam";
  public static readonly string RemoveUserRoles = "RemoveUserRoles";
  public static readonly string Renew = "Renew";
  public static readonly string RenewEntitlement = "RenewEntitlement";
  public static readonly string ReplacePrivileges = "ReplacePrivileges";
  public static readonly string Reschedule = "Reschedule";
  public static readonly string ResetOfflineFilters = "ResetOfflineFilters";
  public static readonly string ResetUserFilters = "ResetUserFilters";
  public static readonly string Retrieve = "Retrieve";
  public static readonly string RetrieveAbsoluteAndSiteCollectionUrl = "RetrieveAbsoluteAndSiteCollectionUrl";
  public static readonly string RetrieveActivePath = "RetrieveActivePath";
  public static readonly string RetrieveAllChildUsers = "RetrieveAllChildUsers";
  public static readonly string RetrieveAllEntities = "RetrieveAllEntities";
  public static readonly string RetrieveAllManagedProperties = "RetrieveAllManagedProperties";
  public static readonly string RetrieveAllOptionSets = "RetrieveAllOptionSets";
  public static readonly string RetrieveApplicationRibbon = "RetrieveApplicationRibbon";
  public static readonly string RetrieveAttribute = "RetrieveAttribute";
  public static readonly string RetrieveAttributeChangeHistory = "RetrieveAttributeChangeHistory";
  public static readonly string RetrieveAuditDetails = "RetrieveAuditDetails";
  public static readonly string RetrieveAuditPartitionList = "RetrieveAuditPartitionList";
  public static readonly string RetrieveAvailableLanguages = "RetrieveAvailableLanguages";
  public static readonly string RetrieveBusinessHierarchy = "RetrieveBusinessHierarchy";
  public static readonly string RetrieveByGroup = "RetrieveByGroup";
  public static readonly string RetrieveByResource = "RetrieveByResource";
  public static readonly string RetrieveByResources = "RetrieveByResources";
  public static readonly string RetrieveByTopIncidentProduct = "RetrieveByTopIncidentProduct";
  public static readonly string RetrieveByTopIncidentSubject = "RetrieveByTopIncidentSubject";
  public static readonly string RetrieveChannelAccessProfilePrivileges = "RetrieveChannelAccessProfilePrivileges";
  public static readonly string RetrieveCurrentOrganization = "RetrieveCurrentOrganization";
  public static readonly string RetrieveDataEncryptionKey = "RetrieveDataEncryptionKey";
  public static readonly string RetrieveDependenciesForDelete = "RetrieveDependenciesForDelete";
  public static readonly string RetrieveDependenciesForUninstall = "RetrieveDependenciesForUninstall";
  public static readonly string RetrieveDependentComponents = "RetrieveDependentComponents";
  public static readonly string RetrieveDeploymentLicenseType = "RetrieveDeploymentLicenseType";
  public static readonly string RetrieveDeprovisionedLanguages = "RetrieveDeprovisionedLanguages";
  public static readonly string RetrieveDuplicates = "RetrieveDuplicates";
  public static readonly string RetrieveEntity = "RetrieveEntity";
  public static readonly string RetrieveEntityChanges = "RetrieveEntityChanges";
  public static readonly string RetrieveEntityKey = "RetrieveEntityKey";
  public static readonly string RetrieveEntityRibbon = "RetrieveEntityRibbon";
  public static readonly string RetrieveExchangeAppointments = "RetrieveExchangeAppointments";
  public static readonly string RetrieveExchangeRate = "RetrieveExchangeRate";
  public static readonly string RetrieveFilteredForms = "RetrieveFilteredForms";
  public static readonly string RetrieveFormattedImportJobResults = "RetrieveFormattedImportJobResults";
  public static readonly string RetrieveFormXml = "RetrieveFormXml";
  public static readonly string RetrieveInstalledLanguagePacks = "RetrieveInstalledLanguagePacks";
  public static readonly string RetrieveInstalledLanguagePackVersion = "RetrieveInstalledLanguagePackVersion";
  public static readonly string RetrieveLicenseInfo = "RetrieveLicenseInfo";
  public static readonly string RetrieveLocLabels = "RetrieveLocLabels";
  public static readonly string RetrieveMailboxTrackingFolders = "RetrieveMailboxTrackingFolders";
  public static readonly string RetrieveManagedProperty = "RetrieveManagedProperty";
  public static readonly string RetrieveMembers = "RetrieveMembers";
  public static readonly string RetrieveMembersBulkOperation = "RetrieveMembersBulkOperation";
  public static readonly string RetrieveMetadataChanges = "RetrieveMetadataChanges";
  public static readonly string RetrieveMissingComponents = "RetrieveMissingComponents";
  public static readonly string RetrieveMissingDependencies = "RetrieveMissingDependencies";
  public static readonly string RetrieveMultiple = "RetrieveMultiple";
  public static readonly string RetrieveOptionSet = "RetrieveOptionSet";
  public static readonly string RetrieveOrganizationResources = "RetrieveOrganizationResources";
  public static readonly string RetrieveParentGroups = "RetrieveParentGroups";
  public static readonly string RetrieveParsedData = "RetrieveParsedData";
  public static readonly string RetrievePersonalWall = "RetrievePersonalWall";
  public static readonly string RetrievePrincipalAccess = "RetrievePrincipalAccess";
  public static readonly string RetrievePrincipalAttributePrivileges = "RetrievePrincipalAttributePrivileges";
  public static readonly string RetrievePrincipalSyncAttributeMappings = "RetrievePrincipalSyncAttributeMappings";
  public static readonly string RetrievePrivilegeSet = "RetrievePrivilegeSet";
  public static readonly string RetrieveProcessInstances = "RetrieveProcessInstances";
  public static readonly string RetrieveProductProperties = "RetrieveProductProperties";
  public static readonly string RetrieveProvisionedLanguagePackVersion = "RetrieveProvisionedLanguagePackVersion";
  public static readonly string RetrieveProvisionedLanguages = "RetrieveProvisionedLanguages";
  public static readonly string RetrieveRecordChangeHistory = "RetrieveRecordChangeHistory";
  public static readonly string RetrieveRecordWall = "RetrieveRecordWall";
  public static readonly string RetrieveRelationship = "RetrieveRelationship";
  public static readonly string RetrieveRequiredComponents = "RetrieveRequiredComponents";
  public static readonly string RetrieveRolePrivileges = "RetrieveRolePrivileges";
  public static readonly string RetrieveSharedPrincipalsAndAccess = "RetrieveSharedPrincipalsAndAccess";
  public static readonly string RetrieveSubGroups = "RetrieveSubGroups";
  public static readonly string RetrieveSubsidiaryTeams = "RetrieveSubsidiaryTeams";
  public static readonly string RetrieveSubsidiaryUsers = "RetrieveSubsidiaryUsers";
  public static readonly string RetrieveTeamPrivileges = "RetrieveTeamPrivileges";
  public static readonly string RetrieveTeams = "RetrieveTeams";
  public static readonly string RetrieveTimestamp = "RetrieveTimestamp";
  public static readonly string RetrieveUnpublished = "RetrieveUnpublished";
  public static readonly string RetrieveUnpublishedMultiple = "RetrieveUnpublishedMultiple";
  public static readonly string RetrieveUserPrivileges = "RetrieveUserPrivileges";
  public static readonly string RetrieveUserQueues = "RetrieveUserQueues";
  public static readonly string RetrieveUserSettings = "RetrieveUserSettings";
  public static readonly string RetrieveVersion = "RetrieveVersion";
  public static readonly string RevertProduct = "RevertProduct";
  public static readonly string Revise = "Revise";
  public static readonly string RevokeAccess = "RevokeAccess";
  public static readonly string Rollup = "Rollup";
  public static readonly string Route = "Route";
  public static readonly string RouteTo = "RouteTo";
  public static readonly string rwb_CustomiseRibbon = "rwb_CustomiseRibbon";
  public static readonly string sbt_AllBacklogInvoicesnedtoSimonOnceoff = "sbt_AllBacklogInvoicesnedtoSimonOnceoff";
  public static readonly string sbt_Deactivation = "sbt_Deactivation";
  public static readonly string Search = "Search";
  public static readonly string SearchByBody = "SearchByBody";
  public static readonly string SearchByBodyLegacy = "SearchByBodyLegacy";
  public static readonly string SearchByKeywords = "SearchByKeywords";
  public static readonly string SearchByKeywordsLegacy = "SearchByKeywordsLegacy";
  public static readonly string SearchByTitle = "SearchByTitle";
  public static readonly string SearchByTitleLegacy = "SearchByTitleLegacy";
  public static readonly string Send = "Send";
  public static readonly string SendFromTemplate = "SendFromTemplate";
  public static readonly string SetBusiness = "SetBusiness";
  public static readonly string SetDataEncryptionKey = "SetDataEncryptionKey";
  public static readonly string SetFeatureStatus = "SetFeatureStatus";
  public static readonly string SetLocLabels = "SetLocLabels";
  public static readonly string SetParent = "SetParent";
  public static readonly string SetProcess = "SetProcess";
  public static readonly string SetRelated = "SetRelated";
  public static readonly string SetReportRelated = "SetReportRelated";
  public static readonly string SetState = "SetState";
  public static readonly string SetStateDynamicEntity = "SetStateDynamicEntity";
  public static readonly string Transform = "Transform";
  public static readonly string TriggerServiceEndpointCheck = "TriggerServiceEndpointCheck";
  public static readonly string UninstallSampleData = "UninstallSampleData";
  public static readonly string UnlockInvoicePricing = "UnlockInvoicePricing";
  public static readonly string UnlockSalesOrderPricing = "UnlockSalesOrderPricing";
  public static readonly string Unpublish = "Unpublish";
  public static readonly string Update = "Update";
  public static readonly string UpdateAttribute = "UpdateAttribute";
  public static readonly string UpdateEntity = "UpdateEntity";
  public static readonly string UpdateFeatureConfig = "UpdateFeatureConfig";
  public static readonly string UpdateOptionSet = "UpdateOptionSet";
  public static readonly string UpdateOptionValue = "UpdateOptionValue";
  public static readonly string UpdateProductProperties = "UpdateProductProperties";
  public static readonly string UpdateRelationship = "UpdateRelationship";
  public static readonly string UpdateSolutionComponent = "UpdateSolutionComponent";
  public static readonly string UpdateStateValue = "UpdateStateValue";
  public static readonly string UpdateUserSettings = "UpdateUserSettings";
  public static readonly string Upsert = "Upsert";
  public static readonly string UtcTimeFromLocalTime = "UtcTimeFromLocalTime";
  public static readonly string Validate = "Validate";
  public static readonly string ValidateRecurrenceRule = "ValidateRecurrenceRule";
  public static readonly string WhoAmI = "WhoAmI";
  public static readonly string Win = "Win";

    }
}

