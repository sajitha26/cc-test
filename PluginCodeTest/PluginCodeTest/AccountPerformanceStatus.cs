using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using PluginCodeTest.ProxyClasses;

namespace PluginCodeTest
{
    public partial class AccountPerformanceStatus : BasePlugin
    {
        public AccountPerformanceStatus(string unsecureConfig, string secureConfig) : base(unsecureConfig, secureConfig)
        {
            // Register for any specific events by instantiating a new instance of the 'PluginEvent' class and registering it
            base.RegisteredEvents.Add(new PluginEvent()
            {
                Stage = eStage.PostOperation,
                MessageName = MessageNames.Update,
                EntityName = EntityNames.account,
                PluginAction = ExecutePluginLogic
            });
        }
        public void ExecutePluginLogic(IServiceProvider serviceProvider)
        {
            // Use a 'using' statement to dispose of the service context properly
            // To use a specific early bound entity replace the 'Entity' below with the appropriate class type
            using (var localContext = new LocalPluginContext<Account>(serviceProvider))
            {
                #region Entity Validation

                // Target check
                if (localContext.TargetEntity == null)
                    throw new Exception("Target does not exist");
                // Entity check
                if (localContext.TargetEntity.LogicalName != EntityNames.account)
                {
                    return;
                }
                //  Message check
                if (localContext.MessageName != "Update")
                {
                    return;
                }

                #endregion

                localContext.Trace(string.Format("=== In AccountPerformanceStatus.ExecutePluginLogic - localContext.TargetEntity.Id: {0}", localContext.TargetEntity.Id));

                //  Get account entity data
                Account account = localContext.PostImage;
                localContext.Trace(string.Format("===  account.AccountName: {0}", account.AccountName));

                if (account.PreviousPerformanceRate.Value == 0m)
                {
                    localContext.Trace(string.Format("=== account.PreviousPerformanceRate.Value: {0}", account.PreviousPerformanceRate.Value));
                    return;
                }
               
                decimal rate_change = Convert.ToDecimal((account.CurrentPerformanceRate.Value - account.PreviousPerformanceRate.Value) / account.PreviousPerformanceRate.Value);
                bool valid = false;

                Account tmpAccount = new Account();
                tmpAccount.AccountId = account.AccountId;

                if (rate_change == 0m)
                {
                    tmpAccount.PerformanceStatus = eAccount_PerformanceStatus.Same;
                    valid = true;
                }
                else if (rate_change < 0m)
                {
                    tmpAccount.PerformanceStatus = eAccount_PerformanceStatus.Declined;
                    valid = true;
                } 
                else if (account.CompanySize == eAccount_CompanySize.Small && rate_change > 0m)
                {
                    tmpAccount.PerformanceStatus = eAccount_PerformanceStatus.Improving;
                    valid = true;
                }
                else if (account.CompanySize == eAccount_CompanySize.Medium && rate_change > 0.1m)
                {
                    tmpAccount.PerformanceStatus = eAccount_PerformanceStatus.Improved;
                    valid = true;
                }
                else if (account.CompanySize == eAccount_CompanySize.Large && rate_change > 0.2m)
                {
                    tmpAccount.PerformanceStatus = eAccount_PerformanceStatus.Improved;
                    valid = true;
                }

                if (valid)
                {
                    localContext.Trace(string.Format("=== Updating new values"));
                    localContext.Trace(string.Format("=== tmpAccount.PerformanceStatus: {0}", tmpAccount.PerformanceStatus));
                    localContext.OrganizationService.Update(tmpAccount);
                }
                else
                {
                    localContext.Trace(string.Format("=== No updates"));
                }
            }
        }
    }
}
