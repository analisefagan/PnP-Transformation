﻿using Microsoft.SharePoint.Client;
using System;
using System.Data;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.UI;

using Microsoft.SharePoint.Client.UserProfiles;
using System.Collections.Generic;
using System.Web.UI.WebControls;
using System.Configuration;
using System.Web.Hosting;

namespace EmployeeRegistration.FormsWeb
{
    public partial class Default : System.Web.UI.Page
    {
        protected void Page_PreInit(object sender, EventArgs e)
        {
            Uri redirectUrl;
            switch (SharePointContextProvider.CheckRedirectionStatus(Context, out redirectUrl))
            {
                case RedirectionStatus.Ok:
                    return;
                case RedirectionStatus.ShouldRedirect:
                    Response.Redirect(redirectUrl.AbsoluteUri, endResponse: true);
                    break;
                case RedirectionStatus.CanNotRedirect:
                    Response.Write("An error occurred while processing your request.");
                    Response.End();
                    break;
            }
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!Page.IsPostBack)
            {
                // Provision supporting artefacts in case that's still needed
                using (var clientContext = GetClientContext())
                {
                    if (!SetupManager.Initialized)
                    {
                        // Provision lists
                        SetupManager.ProvisionLists(clientContext);

                        // upload assets and provision the application
                        ProvisionEmployeeRegistrationApplication(clientContext);

                        SetupManager.Initialized = true;
                    }
                }

                LoadListItems();
            }            
        }

        private void ProvisionEmployeeRegistrationApplication(ClientContext ctx)
        {
            // upload files to the style library
            List styleLibrary = ctx.Web.Lists.GetByTitle("Style Library");
            ctx.Load(styleLibrary, l => l.RootFolder);
            Folder pnpFolder = styleLibrary.RootFolder.EnsureFolder("OfficeDevPnP");

            string fileName = "AppLauncher.js";
            File assetFile = pnpFolder.GetFile(fileName);
            if (assetFile != null)
            {
                assetFile.CheckOut();
            }

            string clientId = ConfigurationManager.AppSettings["ClientId"];
            Uri redirectUri = new Uri(HttpContext.Current.Request.Url.AbsoluteUri);
            string redirectUrl = String.Format("{0}://{1}{2}", redirectUri.Scheme, redirectUri.Authority, redirectUri.LocalPath);

            string localFilePath = "Assets/" + fileName;
            string newLocalFilePath = Utilities.ReplaceTokensInAssetFile(ctx, localFilePath, clientId, redirectUrl);

            assetFile = pnpFolder.UploadFile(fileName, newLocalFilePath, true);
            assetFile.CheckIn("Uploaded by provisioning engine.", CheckinType.MajorCheckIn);
            ctx.ExecuteQuery();
            System.IO.File.Delete(newLocalFilePath);

            Web web = ctx.Web;
            ctx.Load(web, w => w.ServerRelativeUrl);
            ctx.ExecuteQuery();
            Console.WriteLine("");

            // Inject the content editor web parts on the new and edit form pages
            if (!SetupManager.IsWebPartOnPage(ctx, String.Format("{0}/Lists/Employees/newform.aspx", web.ServerRelativeUrl), "Employee Registration"))
            {
                string newFormUrl = string.Format("{0}/{1}", web.ServerRelativeUrl, "Lists/Employees/newform.aspx");
                SetupManager.CloseAllWebParts(ctx, newFormUrl);
                ProvisionWebPart(ctx, newFormUrl, "AppLauncher.js");
            }
            else
            {
                Console.WriteLine("The New form page was already customized");
            }

            if (!SetupManager.IsWebPartOnPage(ctx, String.Format("{0}/Lists/Employees/editform.aspx", web.ServerRelativeUrl), "Employee Registration"))
            {
                string editFormUrl = string.Format("{0}/{1}", web.ServerRelativeUrl, "Lists/Employees/editform.aspx");
                SetupManager.CloseAllWebParts(ctx, editFormUrl);
                ProvisionWebPart(ctx, editFormUrl, "AppLauncher.js");
            }
            else
            {
                Console.WriteLine("The New form page was already customized");
            }
        }

        private void ProvisionWebPart(ClientContext ctx, string relativePageUrl, string scriptFile, bool isWikiPage = false)
        {
            Console.WriteLine("Provisioning web part...");

            string webPartXml = System.IO.File.ReadAllText(HostingEnvironment.MapPath(String.Format("~/{0}", @"Assets\EmployeeRegistration.dwp")));
            //replace tokens
            string scriptUrl = String.Format("~sitecollection/Style Library/OfficeDevPnP/{0}", scriptFile);
            scriptUrl = Utilities.ReplaceTokens(ctx, scriptUrl);
            webPartXml = webPartXml.Replace("%ContentLink%", scriptUrl);

            OfficeDevPnP.Core.Entities.WebPartEntity webPart = new OfficeDevPnP.Core.Entities.WebPartEntity()
            {
                WebPartZone = "Main",
                WebPartIndex = 20,
                WebPartTitle = "Employee Registration",
                WebPartXml = webPartXml
            };

            Console.WriteLine("Adding employee registration web part to " + relativePageUrl);
            if (isWikiPage)
            {
                ctx.Web.AddWebPartToWikiPage(relativePageUrl, webPart, 1, 1, false);
            }
            else
            {
                ctx.Web.AddWebPartToWebPartPage(relativePageUrl, webPart);
            }
            Console.WriteLine("");
        }

        private void LoadListItems()
        {
            using (var clientContext = GetClientContext())
            {
                var web = GetClientContextWeb(clientContext);

                var lstDesignation = web.Lists.GetByTitle("EmpDesignation");
                CamlQuery designationQuery = new CamlQuery();
                designationQuery.ViewXml = "<FieldRef Name='Title'/>";
                var designationItems = lstDesignation.GetItems(designationQuery);
                clientContext.Load(designationItems);

                var lstCountry = web.Lists.GetByTitle("EmpCountry");
                CamlQuery countryQuery = new CamlQuery();
                countryQuery.ViewXml = "<FieldRef Name='Title'/>";
                var countryItems = lstCountry.GetItems(countryQuery);
                clientContext.Load(countryItems);

                clientContext.ExecuteQuery();

                var desingations = from item in designationItems.ToList() select new { Designation = item["Title"] };
                ddlDesignation.DataSource = desingations;
                ddlDesignation.DataBind();
                ddlDesignation.Items.Insert(0, "--Select Designation--");

                var countries = from item in countryItems.ToList() select new { Country = item["Title"] };
                ddlCountry.DataSource = countries;
                ddlCountry.DataBind();
                ddlCountry.Items.Insert(0, "--Select Country--");

                string itemID = Request.QueryString["itemId"];
                
                if (itemID != null)
                {
                    List lstEmployee = web.Lists.GetByTitle("Employees");
                    var emplistItem = lstEmployee.GetItemById(itemID);
                    clientContext.Load(emplistItem);
                    clientContext.ExecuteQuery();

                    if (emplistItem != null)
                    {
                        txtEmpNumber.Text = emplistItem["EmpNumber"].ToString();
                        txtName.Text = emplistItem["Title"].ToString();
                        txtUserID.Text = emplistItem["UserID"].ToString();
                        txtManager.Text = emplistItem["EmpManager"].ToString();
                        ddlDesignation.SelectedValue = emplistItem["Designation"].ToString();
                        
                        string cityVal = emplistItem["Location"].ToString();
                        string stateVal = GetStateValFromCity(clientContext, web, cityVal);
                        string countryVal = GetCountryValFromState(clientContext, web, stateVal);

                        if (countryVal != "")
                        {
                            ddlCountry.SelectedValue = countryVal;
                            LoadStateItems();
                            ddlState.SelectedValue = stateVal;
                            LoadCityItems();
                            ddlCity.SelectedValue = cityVal;
                        }

                        LoadSkills(emplistItem["Skills"].ToString());
                        btnUpdate.Visible = true;
                    } // if (emplistItem != null)
                }
                else
                {
                    PeopleManager peopleManager = new PeopleManager(clientContext);
                    PersonProperties personProperties = peopleManager.GetMyProperties();
                    clientContext.Load(personProperties, p => p.AccountName);

                    clientContext.ExecuteQuery();
                    if (personProperties != null && personProperties.AccountName != null)
                    {
                        txtUserID.Text = personProperties.AccountName;
                    }

                    AddEmptySkill();

                    btnSave.Visible = true;
                }

            } // using (var clientContext 
        }

        private ClientContext GetClientContext()
        {
            var spContext = SharePointContextProvider.Current.GetSharePointContext(Context);

            var clientContext = spContext.CreateUserClientContextForSPHost();

            return clientContext;
        }

        private Web GetClientContextWeb(ClientContext clientContext)
        {
            var web = clientContext.Web;

            return web;
        }

        private void LoadStateItems()
        {
            using (var clientContext = GetClientContext())
            {
                var web = GetClientContextWeb(clientContext);

                var lstState = web.Lists.GetByTitle("EmpState");
                CamlQuery query = new CamlQuery();
                query.ViewXml = string.Format("<View><Query><Where><Eq><FieldRef Name='Country' /><Value Type='String'>{0}</Value></Eq></Where></Query><ViewFields><FieldRef Name='Title'/></ViewFields></View>", ddlCountry.SelectedValue);
                var stateItems = lstState.GetItems(query);
                clientContext.Load(stateItems);

                clientContext.ExecuteQuery();

                var states = from item in stateItems.ToList() select new { State = item["Title"] };
                ddlState.DataSource = states;
                ddlState.DataBind();
                ddlState.Items.Insert(0, "--Select State--");
            }
           
        }

        private string GetStateValFromCity(ClientContext clientContext, Web web, string cityVal)
        {
            string stateVal = string.Empty;
            List lstCity = web.Lists.GetByTitle("EmpCity");

            CamlQuery query = new CamlQuery();
            query.ViewXml = string.Format("<View><Query><Where><Eq><FieldRef Name='Title' /><Value Type='Text'>{0}</Value></Eq></Where></Query></View>", cityVal);

            var cityItems = lstCity.GetItems(query);
            clientContext.Load(cityItems);
            clientContext.ExecuteQuery();

            if (cityItems.Count > 0)
            {
                stateVal = (cityItems[0]["State"] as FieldLookupValue).LookupValue;
            }

            return stateVal;
        }

        private string GetCountryValFromState(ClientContext clientContext, Web web, string stateVal)
        {
            string countryVal = string.Empty;
            List lstSate = web.Lists.GetByTitle("EmpState");

            CamlQuery query = new CamlQuery();
            query.ViewXml = string.Format("<View><Query><Where><Eq><FieldRef Name='Title' /><Value Type='Text'>{0}</Value></Eq></Where></Query></View>", stateVal);

            var stateItems = lstSate.GetItems(query);
            clientContext.Load(stateItems);
            clientContext.ExecuteQuery();

            if (stateItems.Count > 0)
            {
                countryVal = (stateItems[0]["Country"] as FieldLookupValue).LookupValue;
            }

            return countryVal;
        }

        protected void ddlCountry_SelectedIndexChanged(object sender, EventArgs e)
        {
            LoadStateItems();
            LoadCityItems();
        }

        private void LoadCityItems()
        {
            using (var clientContext = GetClientContext())
            {
                var web = GetClientContextWeb(clientContext);

                var lstCity = web.Lists.GetByTitle("EmpCity");
                CamlQuery query = new CamlQuery();
                query.ViewXml = string.Format("<View><Query><Where><Eq><FieldRef Name='State' /><Value Type='String'>{0}</Value></Eq></Where></Query><ViewFields><FieldRef Name='Title'/></ViewFields></View>", ddlState.SelectedValue);
                var cityItems = lstCity.GetItems(query);
                clientContext.Load(cityItems);

                clientContext.ExecuteQuery();

                var cities = from item in cityItems.ToList() select new { City = item["Title"] };
                ddlCity.DataSource = cities;
                ddlCity.DataBind();
                ddlCity.Items.Insert(0, "--Select City--");
            }      
        }

        protected void ddlState_SelectedIndexChanged(object sender, EventArgs e)
        {
            LoadCityItems();
        }
        protected void lnkAddSkills_Click(object sender, EventArgs e)
        {
            AddNewSkill();
        }

        private void AddEmptySkill()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Technology", typeof(string));
            table.Columns.Add("Experience", typeof(string));

            table.Rows.Add("", "");
            rptSkills.DataSource = table;
            rptSkills.DataBind();
        }

        private void AddNewSkill()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Technology", typeof(string));
            table.Columns.Add("Experience", typeof(string));
            
            RepeaterItemCollection skills = rptSkills.Items;
            foreach (RepeaterItem skill in skills)
            {
                TextBox tbTech = (TextBox)skill.FindControl("rptTxtTechnology");
                TextBox tbSkill = (TextBox)skill.FindControl("rptTxtExperience");
                table.Rows.Add(tbTech.Text, tbSkill.Text);
            }

            table.Rows.Add("", "");
            rptSkills.DataSource = table;
            rptSkills.DataBind();
        }

        private void LoadSkills(string skillsData)
        {
            string[] skills = skillsData.Split(';');
            DataTable table = new DataTable();
            table.Columns.Add("Technology", typeof(string));
            table.Columns.Add("Experience", typeof(string));

            foreach (string skillData in skills)
            {
                if (skillData != "")
                {
                    string[] skill = skillData.Split(',');
                    table.Rows.Add(skill[0], skill[1]);
                }
            }

            rptSkills.DataSource = table;
            rptSkills.DataBind();
        }

        protected void btnSave_Click(object sender, EventArgs e)
        {
            string errMsg = string.Empty;

            try
            {
                using (var clientContext = GetClientContext())
                {
                    var web = GetClientContextWeb(clientContext);

                    var lstEmployee = web.Lists.GetByTitle("Employees");
                    var itemCreateInfo = new ListItemCreationInformation();
                    var listItem = lstEmployee.AddItem(itemCreateInfo);
                    
                    listItem["EmpNumber"] = txtEmpNumber.Text;
                    listItem["UserID"] = txtUserID.Text;
                    listItem["Title"] = txtName.Text;
                    listItem["EmpManager"] = txtManager.Text;
                    listItem["Designation"] = ddlDesignation.SelectedValue;
                    listItem["Location"] = ddlCity.Text;
                    
                    StringBuilder sbSkills = new StringBuilder();
                    RepeaterItemCollection skills = rptSkills.Items;
                    foreach (RepeaterItem skill in skills)
                    {
                        TextBox tbTech = (TextBox)skill.FindControl("rptTxtTechnology");
                        TextBox tbSkill = (TextBox)skill.FindControl("rptTxtExperience");
                        sbSkills.Append(tbTech.Text).Append(",").Append(tbSkill.Text).Append(";");
                    }
                    
                    listItem["Skills"] = sbSkills.ToString();

                    listItem.Update();
                    clientContext.ExecuteQuery();
                }
            }
            catch (Exception ex)
            {
                errMsg = ex.Message;
            }

            if (errMsg.Length > 0)
            {
                ClientScript.RegisterStartupScript(this.GetType(), "errorMessage", "alert('" + errMsg + "');", true);
            }
            else
            {
                string url = HttpContext.Current.Request.Url.AbsoluteUri;
                url = url.Replace("Default.aspx", "Thanks.aspx");
                Response.Redirect(url);
            }
        }

        protected void btnGetManager_Click(object sender, EventArgs e)
        {
            using (var clientContext = GetClientContext())
            {
                string[] propertyNames = { "FirstName", "LastName", "Manager" };
                string accountName = txtUserID.Text;

                PeopleManager peopleManager = new PeopleManager(clientContext);
                UserProfilePropertiesForUser prop = new UserProfilePropertiesForUser(clientContext, accountName, propertyNames);
                IEnumerable<string> profileProperty = peopleManager.GetUserProfilePropertiesFor(prop);
                clientContext.Load(prop);
                clientContext.ExecuteQuery();

                if (profileProperty != null && profileProperty.Count() > 0)
                {
                    txtName.Text = string.Format("{0} {1}", profileProperty.ElementAt(0), profileProperty.ElementAt(1));
                    txtManager.Text = profileProperty.ElementAt(2);
                }
                else
                {
                    string noProfileData = string.Format("No data found for user id: {0}", accountName.Replace(@"\",@"\\"));
                    ClientScript.RegisterStartupScript(this.GetType(), "errorMessage", "alert('" + noProfileData + "');", true);
                }
            }
        }

        protected void btnUpdate_Click(object sender, EventArgs e)
        {
            string itemID = Request.QueryString["itemId"];
            string employeeListName = "Employees";
            string SPHostUrl = string.Format("{0}/Lists/{1}", Request.QueryString["SPHostUrl"], employeeListName);

            if (itemID != null)
            {
                string errMsg = string.Empty;

                try
                {
                    using (var clientContext = GetClientContext())
                    {
                        var web = GetClientContextWeb(clientContext);

                        List lstEmployee = clientContext.Web.Lists.GetByTitle(employeeListName);
                        var listItem = lstEmployee.GetItemById(itemID);
                        
                        listItem["EmpNumber"] = txtEmpNumber.Text;
                        listItem["UserID"] = txtUserID.Text;
                        listItem["Title"] = txtName.Text;
                        listItem["EmpManager"] = txtManager.Text;
                        listItem["Designation"] = ddlDesignation.SelectedValue;
                        listItem["Location"] = ddlCity.Text;

                        StringBuilder sbSkills = new StringBuilder();
                        RepeaterItemCollection skills = rptSkills.Items;
                        foreach (RepeaterItem skill in skills)
                        {
                            TextBox tbTech = (TextBox)skill.FindControl("rptTxtTechnology");
                            TextBox tbSkill = (TextBox)skill.FindControl("rptTxtExperience");
                            sbSkills.Append(tbTech.Text).Append(",").Append(tbSkill.Text).Append(";");
                        }

                        listItem["Skills"] = sbSkills.ToString();

                        listItem.Update();
                        clientContext.ExecuteQuery();
                    }
                }
                catch (Exception ex)
                {
                    errMsg = ex.Message;
                }

                if (errMsg.Length > 0)
                {
                    ClientScript.RegisterStartupScript(this.GetType(), "errorMessage", "alert('" + errMsg + "');", true);
                }
                else
                {
                    Response.Redirect(SPHostUrl);
                }
            }
        }
       
    }
}