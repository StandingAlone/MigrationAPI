using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using log4net;
using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client.UserProfiles;

namespace MigrationApiDemo
{
    public class ManifestPackage
    {
        private readonly SharePointMigrationTarget _target;
        private readonly SharePointMigrationSource _source;
        private Dictionary<string, string> _targetColumnChange;
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        List<User> usersColl = null;
        private Boolean _isDifferentColumn = ConfigurationManager.AppSettings["IsDestinationListHaveDifferentColumn"] == "Yes" ? true : false;
        private ClientContext _sourceClientContext;
        public ManifestPackage(SharePointMigrationTarget sharePointMigrationTarget, SharePointMigrationSource sharePointMigrationSource)
        {
            _target = sharePointMigrationTarget;
            _source = sharePointMigrationSource;
        }
        public IEnumerable<MigrationPackageFile> GetManifestPackageFiles(ListItemCollection sourceItemCollections, string listName, ClientContext context)
        {
            Log.Debug("Generating manifest package");
            // get Target ChangedColumns
            _sourceClientContext = context;
            var result = new[]
            {
                GetExportSettingsXml(),
                GetLookupListMapXml(),
                GetManifestXml(sourceItemCollections, listName, context),
                GetRequirementsXml(),
                GetRootObjectMapXml(),
                GetSystemDataXml(),
                GetUserGroupXml(context),
                //GetViewFormsListXml()
            };

            Log.Debug($"Generated manifest package containing {result.Length} files, total size: {result.Select(x => x.Contents.Length).Sum() / 1024.0 / 1024.0:0.00}mb");

            return result;
        }

        private MigrationPackageFile GetExportSettingsXml()
        {
            //var exportSettingsDefaultXml = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<ExportSettings SiteUrl=\"{_target.SiteName}\" FileLocation=\"C:\\Temp\\0 FilesToUpload\" IncludeSecurity=\"None\" xmlns=\"urn:deployment-exportsettings-schema\" />");
            //return new MigrationPackageFile { Filename = "ExportSettings.xml", Contents = exportSettingsDefaultXml };
            //var exportSettingsDefaultXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n";
            //exportSettingsDefaultXml += $"<ExportSettings SiteUrl=\"{_target._tenantUrl}\" FileLocation=\"C:\\Temp\\0 FilesToUpload\" IncludeSecurity=\"All\" xmlns=\"urn:deployment-exportsettings-schema\" />";
            //return new MigrationPackageFile { Filename = "ExportSettings.xml", Contents = Encoding.UTF8.GetBytes(exportSettingsDefaultXml) };
            var exportSettingsDefaultXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n";
            var siteUrl = _source._siteUrl;
            exportSettingsDefaultXml += $"<ExportSettings SiteUrl=\"{siteUrl}\" IncludeSecurity=\"All\" SourceType=\"SharePoint\" xmlns=\"urn:deployment-exportsettings-schema\" />";
            return new MigrationPackageFile { Filename = "ExportSettings.xml", Contents = Encoding.UTF8.GetBytes(exportSettingsDefaultXml) };
        }

        private MigrationPackageFile GetLookupListMapXml()
        {
            //var lookupListMapDefaultXml = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<LookupLists xmlns=\"urn:deployment-lookuplistmap-schema\" />");
            //return new MigrationPackageFile { Filename = "LookupListMap.xml", Contents = lookupListMapDefaultXml };
            LookupList lookup = new LookupList();
            var lookupListMapDefaultXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n";
            lookupListMapDefaultXml += "<LookupLists xmlns=\"urn:deployment-lookuplistmap-schema\">";
            // loop the destionation fields
            foreach (Field field in _target._fields)
            {
                // check the type of field
                string fieldType = field.TypeAsString;
                if (!field.Hidden && !field.ReadOnlyField && (fieldType == "Lookup" || fieldType == "LookupMulti"))
                {
                    // Get the lookuplist id and it's items from dictionary based on column internal name. 
                    LookupList lp = _target.lookupListDic[field.InternalName];
                    lookupListMapDefaultXml += $"<LookupList Id=\"{lp.listId}\" Url=\"{lp.itemArray[0].FieldValues["FileDirRef"]}\" Included=\"false\">";
                    lookupListMapDefaultXml += "<LookupItems>";
                    // loop through each lookuplist items
                    foreach (ListItem item in lp.itemArray)
                    {
                        
                        lookupListMapDefaultXml += $"<LookupItem Id=\"{item.Id}\" DocId=\"{item["UniqueId"]}\" Url=\"{item["FileRef"]}\" Included=\"false\" />";
                    }
                    lookupListMapDefaultXml += "</LookupItems>";
                    lookupListMapDefaultXml += "</LookupList>";
                }
            }
            lookupListMapDefaultXml += "</LookupLists>";
            return new MigrationPackageFile { Filename = "LookupListMap.xml", Contents = Encoding.UTF8.GetBytes(lookupListMapDefaultXml) };
        }

        private MigrationPackageFile GetRequirementsXml()
        {
            var requirementsDefaultXml = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Requirements xmlns=\"urn:deployment-requirements-schema\" />");
            return new MigrationPackageFile { Filename = "Requirements.xml", Contents = requirementsDefaultXml };
        }

        private MigrationPackageFile GetRootObjectMapXml()
        {
            var objectRootMapDefaultXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n";
            objectRootMapDefaultXml += "<RootObjects xmlns=\"urn:deployment-rootobjectmap-schema\">";
            objectRootMapDefaultXml += $"<RootObject Id=\"{_target.ListId}\" Type=\"List\" ParentId=\"{_target.WebId}\" WebUrl=\"{_target.SiteName}\" Url=\"{string.Format($"{_target.SiteName}/Lists/{_target.ListName}", _target.SiteName, _target.ListName)}\" IsDependency=\"false\" />";
            objectRootMapDefaultXml += "</RootObjects>";
            return new MigrationPackageFile { Filename = "RootObjectMap.xml", Contents = Encoding.UTF8.GetBytes(objectRootMapDefaultXml) };
        }

        private MigrationPackageFile GetSystemDataXml()
        {
            var systemDataXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                                "<SystemData xmlns=\"urn:deployment-systemdata-schema\">" +
                                "<SchemaVersion Version=\"15.0.0.0\" Build=\"16.0.3111.1200\" DatabaseVersion=\"11552\" SiteVersion=\"15\" ObjectsProcessed=\"97\" />" +
                                "<ManifestFiles>" +
                                "<ManifestFile Name=\"Manifest.xml\" />" +
                                "</ManifestFiles>" +
                                "<SystemObjects>" +
                                "</SystemObjects>" +
                                "<RootWebOnlyLists />" +
                                "</SystemData>";
            return new MigrationPackageFile { Filename = "SystemData.xml", Contents = Encoding.UTF8.GetBytes(systemDataXml) };
        }

        private MigrationPackageFile GetUserGroupXml(ClientContext context)
        {
            //var userGroupDefaultXml = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<UserGroupMap xmlns=\"urn:deployment-usergroupmap-schema\"><Users /><Groups /></UserGroupMap>");
            //return new MigrationPackageFile { Filename = "UserGroup.xml", Contents = userGroupDefaultXml };
            //var resourResult = Common.GetResourceCategorization(context);
            // Added current login user
            var currentUser = context.Web.CurrentUser;
            context.Load(currentUser);
            context.ExecuteQuery();
            if (!(usersColl.Any(a => a.Id == currentUser.Id)))
            {
                User user = new User();
                user.Id = currentUser.Id;
                user.emailId = currentUser.Email;
                user.name = currentUser.Title;
                usersColl.Add(user);
            }
            // Get user data from UserInfoList
            var userInfoResults = SPData.getUserInfoUserProperties(context, usersColl);
            // Get user data from userprofile
            var userProfilePropertiesResults = SPData.GetMultipleUsersProfileProperties(context, usersColl, userInfoResults);
            var userGroupDefaultXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n";
            userGroupDefaultXml += "<UserGroupMap xmlns =\"urn:deployment-usergroupmap-schema\">";
            userGroupDefaultXml += "<Users>";
            foreach (var kvp in userInfoResults)
            {
                if (kvp.Value.FieldValues.Count > 0 && kvp.Value["EMail"] != null)
                {
                    var isSiteAdmin = Convert.ToBoolean(kvp.Value["IsSiteAdmin"]) ? "true" : "false";
                    var deleted = Convert.ToBoolean(kvp.Value["Deleted"]) ? "true" : "false";
                    var userProp = userProfilePropertiesResults[kvp.Value["EMail"].ToString()];
                    var isUserProfilePresent = userProp.ServerObjectIsNull == false;
                    if (kvp.Value.ServerObjectIsNull.HasValue && !kvp.Value.ServerObjectIsNull.Value && isUserProfilePresent)
                    {
                        //byte[] bytes = Encoding.UTF8.GetBytes(userProp.UserProfileProperties["SID"]);
                        //var systemId = Convert.ToBase64String(bytes);
                        //Log.Info("SystemID: " + systemId);s
                        userGroupDefaultXml += $"<User Id=\"{kvp.Value["ID"].ToString()}\" Name=\"{kvp.Value["Title"].ToString()}\" Login=\"{kvp.Value["Name"].ToString()}\"  IsDomainGroup=\"false\" IsSiteAdmin=\"{isSiteAdmin}\" IsDeleted=\"{deleted}\" SystemId=\"\" Flags=\"0\" />";
                    }
                    else
                    {
                        var newGuid = Guid.NewGuid().ToString();
                        var systemId = newGuid.Replace("-", "");
                        userGroupDefaultXml += $"<User Id=\"{kvp.Key}\" Name=\"{kvp.Value["Title"].ToString()}\" Login=\"{kvp.Value["Name"].ToString()}\"  IsDomainGroup=\"false\" IsSiteAdmin=\"false\" IsDeleted=\"true\" Flags=\"0\" SystemId=\"{systemId}\"/>";
                    }

                }
                else
                {
                    var userData = usersColl.Where(a => a.Id == kvp.Key);
                    foreach (var user in userData)
                    {
                        var newGuid = Guid.NewGuid().ToString();
                        var systemId = newGuid.Replace("-", "");
                        if (user.name != "System Account")
                        {
                            userGroupDefaultXml += $"<User Id=\"{kvp.Key}\" Name=\"{user.name}\" Login=\"{user.name}\" IsDomainGroup=\"true\" IsSiteAdmin=\"false\" IsDeleted=\"true\" Flags=\"0\" SystemId=\"{systemId}\"/>";
                        }
                        else // If user name is System Account
                        {
                            userGroupDefaultXml += $"<User Id=\"{kvp.Key}\" Name=\"{user.name}\" Login=\"SHAREPOINT\\system\" IsDomainGroup=\"false\" IsSiteAdmin=\"false\" IsDeleted=\"false\" Flags=\"0\" SystemId=\"\"/>";
                        }
                    }
                }
            }
            userGroupDefaultXml += "</Users>";
            userGroupDefaultXml += "<Groups />";
            userGroupDefaultXml += "</UserGroupMap>";
            return new MigrationPackageFile
            {
                Filename = "UserGroup.xml",
                Contents = Encoding.UTF8.GetBytes(userGroupDefaultXml)
            };
        }

        private MigrationPackageFile GetViewFormsListXml()
        {
            var viewFormsListDefaultXml = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<ViewFormsList xmlns=\"urn:deployment-viewformlist-schema\" />");
            return new MigrationPackageFile { Filename = "ViewFormsList.xml", Contents = viewFormsListDefaultXml };
        }

        private MigrationPackageFile GetManifestXml(ListItemCollection listItems, string listName, ClientContext context)
        {
            var webUrl = $"{_target.SiteName}";
            var listLocation = $"{webUrl}/Lists/{_target.ListName}";
            var rootNode = new SPGenericObjectCollection1();
            FieldCollection fields = SPData.GetFields(context, listName);
            //FieldCollection destinationFields = SPData.GetFields(context, "ClientLegalEntityCT");
            usersColl = new List<User>();
            //var rootfolder = GetSPRootFolder(webUrl, listLocation);
            //rootNode.SPObject.Add(rootfolder);

            //var attachmentId = Guid.NewGuid();
            //string attachmentnames = "Attachments";
            //var attachementFolderNode = GetSPFolder(attachmentId, webUrl, listLocation + "/" + attachmentnames, attachmentnames);
            //rootNode.SPObject.Add(attachementFolderNode);

            //var folderId = Guid.NewGuid();
            //string folderName = "Folder";
            //var folderNode = GetSPFolder(folderId, webUrl, listLocation + "/" + folderName, folderName);
            //rootNode.SPObject.Add(folderNode);

            //var itemFolderId = Guid.NewGuid();
            //string itemFolderName = "Item";
            //var itemFolderNode = GetSPFolder(itemFolderId, webUrl, listLocation + "/" + itemFolderName, itemFolderName);
            //rootNode.SPObject.Add(itemFolderNode);

            //var list = GetSPList(webUrl, listLocation);
            //rootNode.SPObject.Add(list);
            _targetColumnChange = GetChangeColumnNames();
            foreach (var listItem in listItems)
            {
                var spListItemContainerId = Guid.NewGuid();
                var spListItemContainer = GetSPListItem(webUrl, spListItemContainerId, listItem, fields);
                var versionObj = new SPListItemVersionCollection();
                var versions = listItem.Versions;
                context.Load(versions);
                context.ExecuteQuery();
                if (versions.Count > 1)
                {
                    for (int i = versions.Count - 1; i >= 0; i--)
                    {
                        var version = versions[i];
                        var versItem = version.FieldValues;
                        SPListItem spListItem = new SPListItem();
                        spListItem.Id = spListItemContainerId.ToString();
                        spListItem.ParentWebId = _target.WebId.ToString();
                        spListItem.ParentListId = _target.ListId.ToString();
                        spListItem.Name = listItem["FileLeafRef"].ToString();
                        spListItem.DirName = _target.SiteName + "/Lists/" + _target.ListName;//todo Migration: are we allways storing in documents directory?
                        spListItem.IntId = Convert.ToInt32(listItem["ID"]);
                        spListItem.Version = versItem["_UIVersionString"].ToString();
                        spListItem.ContentTypeId = versItem["ContentTypeId"].ToString();
                        spListItem.Author = Common.GetSingleId(usersColl, versItem, "Author", false);
                        spListItem.ModifiedBy = Common.GetSingleId(usersColl, versItem, "Editor", false);
                        spListItem.TimeLastModified = Common.ValidXMLDate(versItem["Modified"].ToString()); // "2018-11-28T11:29:06"
                        spListItem.TimeCreated = Common.ValidXMLDate(listItem["Created"].ToString());
                        spListItem.ModerationStatus = SPModerationStatusType.Approved;
                        var spfields = new SPFieldCollection();
                        var versionFields = version.Fields;
                        context.Load(versionFields);
                        context.ExecuteQuery();
                        foreach (var field in versionFields)
                        {
                            string fieldType = field.TypeAsString;
                            if (!field.ReadOnlyField && !field.Hidden && field.InternalName != "ContentType" && field.InternalName != "Attachments" && field.InternalName != "Predecessors")
                            {
                                var spfield = new SPField();
                                var isMultiValueTaxField = false; //todo
                                var isTaxonomyField = false; //todo
                                if (isMultiValueTaxField)
                                {
                                    //todo
                                    //spfield.Name = [TaxHiddenFieldName];
                                    //spfield.Value = "[guid-of-hidden-field]|[text-value];[guid-of-hidden-field]|[text-value2];";
                                    //spfield.Type = "Note"; 
                                }
                                else if (isTaxonomyField)
                                {
                                    //todo
                                    //spfield.Name = [TaxHiddenFieldName];
                                    //spfield.Value = [Value] + "|" + [TaxHiddenFieldValue];
                                    //spfield.Type = "Note"; 
                                }
                                else
                                {
                                    spfield.Name = _isDifferentColumn && _targetColumnChange.ContainsKey(field.InternalName + ";#" + fieldType) ? _targetColumnChange[field.InternalName + ";#" + fieldType] : field.InternalName;
                                    switch (fieldType)
                                    {
                                        case "User":
                                            spfield.Value = Common.GetSingleId(usersColl, versItem, field.InternalName, true);
                                            break;
                                        case "MultiUser":
                                            spfield.Name = _isDifferentColumn && _targetColumnChange.ContainsKey(field.InternalName + ";#" + fieldType) ? _targetColumnChange[field.InternalName + ";#" + fieldType] : field.InternalName;
                                            break;
                                        case "Lookup":
                                            spfield.Value = Common.GetLookUpId(versItem, field.InternalName, _target.lookupListDic, true);
                                            break;
                                        case "LookupMulti":
                                            spfield.Value = Common.GetLookUpId(versItem, field.InternalName, _target.lookupListDic, false);
                                            break;
                                        default:
                                            spfield.Value = versItem[field.InternalName] != null ? versItem[field.InternalName].ToString() : "";
                                            break;
                                    }
                                }
                                spfields.Field.Add(spfield);
                            }
                        }
                        spListItem.Items.Add(spfields);
                        versionObj.ListItem.Add(spListItem);
                    }
                    ((SPListItem)spListItemContainer.Item).Items.Add(versionObj);
                }
                rootNode.SPObject.Add(spListItemContainer);
            }
            var serializer = new XmlSerializer(typeof(SPGenericObjectCollection1));
            var settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.Encoding = Encoding.UTF8;
            //settings.OmitXmlDeclaration = false;
            var users = new SPUserResourceValues();
            using (var memoryStream = new MemoryStream())
            using (var xmlWriter = XmlWriter.Create(memoryStream, settings))
            {
                serializer.Serialize(xmlWriter, rootNode);
                return new MigrationPackageFile
                {
                    Contents = memoryStream.ToArray(),
                    Filename = "Manifest.xml"
                };
            }
        }
        private SPGenericObject GetSPRootFolder(string webUrl, string listLocation)
        {
            SPGenericObject spfolder = new SPGenericObject();
            spfolder.Id = _target.RootFolderId.ToString();
            spfolder.ObjectType = SPObjectType.SPFolder;
            spfolder.ParentId = _target.RootFolderParentId.ToString();
            spfolder.ParentWebId = _target.WebId.ToString();
            spfolder.ParentWebUrl = webUrl;
            spfolder.Url = listLocation;
            SPFolder folder = new SPFolder();
            folder.Id = _target.RootFolderId.ToString();
            folder.Url = "Lists/" + _target.ListName;
            folder.Name = _target.ListName;
            folder.ParentFolderId = _target.RootFolderParentId.ToString();
            folder.ParentWebId = _target.WebId.ToString();
            folder.ParentWebUrl = webUrl;
            folder.ContainingDocumentLibrary = _target.ListId.ToString();
            folder.TimeCreated = DateTime.Now;
            folder.TimeLastModified = DateTime.Now;
            folder.SortBehavior = "1";
            folder.Properties = null;
            spfolder.Item = folder;
            return spfolder;
        }

        private SPGenericObject GetSPFolder(Guid id, string webUrl, string url, string name)
        {
            SPGenericObject spfolder = new SPGenericObject();
            spfolder.Id = id.ToString();
            spfolder.ObjectType = SPObjectType.SPFolder;
            spfolder.ParentId = _target.RootFolderId.ToString();
            spfolder.ParentWebId = _target.WebId.ToString();
            spfolder.ParentWebUrl = webUrl;
            spfolder.Url = url;
            SPFolder folder = new SPFolder();
            folder.Id = id.ToString();
            folder.Url = "Lists/" + _target.ListName + "/" + name;
            folder.Name = name;
            folder.ParentFolderId = _target.RootFolderId.ToString();
            folder.ParentWebId = _target.WebId.ToString();
            folder.ParentWebUrl = webUrl;
            folder.ContainingDocumentLibrary = _target.ListId.ToString();
            folder.TimeCreated = DateTime.Now;
            folder.TimeLastModified = DateTime.Now;
            folder.SortBehavior = "1";
            folder.Properties = null;
            spfolder.Item = folder;
            return spfolder;
        }
        private SPGenericObject GetSPList(string webUrl, string listLocation)
        {
            SPGenericObject spList = new SPGenericObject();
            spList.Id = _target.ListId.ToString();
            spList.ObjectType = SPObjectType.SPList;
            spList.ParentId = _target.WebId.ToString();
            spList.ParentWebId = _target.WebId.ToString();
            spList.ParentWebUrl = webUrl;
            spList.Url = listLocation;
            SPList list = new SPList();
            list.Id = _target.ListId.ToString();
            list.RootFolderUrl = listLocation;
            list.ParentWebId = _target.WebId.ToString();
            list.ParentWebUrl = webUrl;
            list.RootFolderId = _target.RootFolderId.ToString();
            list.Title = _target.ListName;
            list.BaseType = SPBaseType.GenericList;
            list.BaseTemplate = "GenericList";
            spList.Item = list;
            return spList;
        }
        private SPGenericObject GetSPListItem(string webUrl, Guid spListItemContainerId, ListItem listItem, FieldCollection fields)
        {
            SPGenericObject spListItemContainer = new SPGenericObject();
            spListItemContainer.Id = spListItemContainerId.ToString();
            spListItemContainer.ObjectType = SPObjectType.SPListItem;
            spListItemContainer.ParentId = _target.ListId.ToString();
            spListItemContainer.ParentWebId = _target.WebId.ToString();
            spListItemContainer.ParentWebUrl = webUrl;
            spListItemContainer.Url = listItem["FileRef"].ToString();
            var newGuid = Guid.NewGuid();
            SPListItem spListItem = new SPListItem();
            spListItem.Id = spListItemContainerId.ToString();
            spListItem.FileUrl = "Lists/" + _target.ListName + "/" + listItem["FileLeafRef"].ToString();
            spListItem.DocType = ListItemDocType.File;
            spListItem.Name = listItem["FileLeafRef"].ToString();
            spListItem.DirName = _target.SiteName + "/Lists/" + _target.ListName; //todo Migration: are we allways storing in documents directory?
            spListItem.ParentWebId = _target.WebId.ToString();
            spListItem.ParentFolderId = _target.RootFolderId.ToString();
            spListItem.ParentListId = _target.ListId.ToString();
            spListItem.IntId = Convert.ToInt32(listItem["ID"]);
            //spListItem.DocId = listItem["UniqueId"].ToString();
            spListItem.DocId = newGuid.ToString();
            spListItem.TimeCreated = Common.ValidXMLDate(listItem["Created"].ToString());
            spListItem.TimeLastModified = Common.ValidXMLDate(listItem["Modified"].ToString()); // "2018-11-28T11:29:06"
            spListItem.Version = listItem["_UIVersionString"].ToString();
            spListItem.Author = Common.GetSingleId(usersColl, listItem, "Author", false);
            spListItem.ModifiedBy = Common.GetSingleId(usersColl, listItem, "Editor", false);
            spListItem.Order = Convert.ToInt32(listItem["ID"]) * 100;
            spListItem.ModerationStatus = SPModerationStatusType.Approved;
            spListItem.ContentTypeId = listItem["ContentTypeId"].ToString();
            spListItemContainer.Item = spListItem;
            var spfields = GetFields(fields, listItem, spListItem);
            ((SPListItem)spListItemContainer.Item).Items.Add(spfields);
            return spListItemContainer;
        }
        private SPFieldCollection GetFields(FieldCollection fields, ListItem listItem, SPListItem spListItem)
        {
            SPFieldCollection spfields = new SPFieldCollection();
            var sp2field = new SPField();
            sp2field.Name = "ContentTypeId";
            sp2field.Value = listItem["ContentTypeId"].ToString();
            var sp3field = new SPField();
            sp3field.Name = "ContentType";
            sp3field.Value = "Item";
            spfields.Field.Add(sp2field);
            spfields.Field.Add(sp3field);
            foreach (var field in fields)
            {
                string fieldType = field.TypeAsString;
                if (!field.Hidden && field.InternalName != "Attachments"
                    && field.InternalName != "ContentType"
                    && fieldType != "Computed" && field.InternalName != "ComplianceAssetId" && field.InternalName != "ID"
                    && field.InternalName != "_UIVersionString" && field.InternalName != "ItemChildCount"
                    && field.InternalName != "FolderChildCount" && field.InternalName != "_ComplianceFlags"
                    && field.InternalName != "_ComplianceTag" && field.InternalName != "_ComplianceTagWrittenTime"
                    && field.InternalName != "_ComplianceTagUserId" && field.InternalName != "AppAuthor" && field.InternalName != "AppEditor" && field.InternalName!= "Predecessors")
                {
                    var spfield = new SPField();
                    var isMultiValueTaxField = false; //todo
                    var isTaxonomyField = false; //todo
                    if (isMultiValueTaxField)
                    {
                        //todo
                        //spfield.Name = [TaxHiddenFieldName];
                        //spfield.Value = "[guid-of-hidden-field]|[text-value];[guid-of-hidden-field]|[text-value2];";
                        //spfield.Type = "Note"; 
                    }
                    else if (isTaxonomyField)
                    {
                        //todo
                        //spfield.Name = [TaxHiddenFieldName];
                        //spfield.Value = [Value] + "|" + [TaxHiddenFieldValue];
                        //spfield.Type = "Note"; 
                    }
                    else
                    {
                        spfield.Name = _isDifferentColumn && _targetColumnChange.ContainsKey(field.InternalName + ";#" + fieldType) ? _targetColumnChange[field.InternalName + ";#" + fieldType] : field.InternalName;
                        switch (fieldType)
                        {
                            case "User":
                                spfield.Value = Common.GetSingleId(usersColl, listItem, field.InternalName, true);
                                break;
                            case "MultiUser":
                                spfield.Value = Common.GetMultipleId(usersColl, listItem, field.InternalName);
                                break;
                            case "Lookup":
                                spfield.Value = Common.GetLookUpId(listItem, field.InternalName, _target.lookupListDic, true); 
                                break;
                            case "LookupMulti":
                                spfield.Value = Common.GetLookUpId(listItem, field.InternalName, _target.lookupListDic, false);
                                break;
                            default:
                                spfield.Value = listItem[field.InternalName] != null ? listItem[field.InternalName].ToString() : "";
                                break;
                        }
                    }
                    spfields.Field.Add(spfield);
                }
            }

            return spfields;
        }

        private Dictionary<string, string> GetChangeColumnNames()
        {
            Dictionary<String, string> targetedChangedColumns = new Dictionary<string, string>();
            //Adding for Priority Single line field.
            targetedChangedColumns.Add("Priority;#Text", "PriorityST");
            //Adding for FirstName single line field.
            targetedChangedColumns.Add("FirstName;#Text", "FirstNameST");
            //Adding for Name single line field.
            targetedChangedColumns.Add("Name;#Text", "NameST");
            //Adding for UserName single line field.
            targetedChangedColumns.Add("UserName;#Text", "UserNameST");
            //Adding for Department single line field.
            targetedChangedColumns.Add("Department;#Text", "DepartmentST");
            //Adding for comment mulitline field.
            targetedChangedColumns.Add("Comments;#Note", "CommentsMT");
            //Adding for Description mulitline field.
            targetedChangedColumns.Add("Description;#Note", "DescriptionMT");
            //Adding for Notes mulitline field.
            targetedChangedColumns.Add("Notes;#Note", "NotesMT");
            //Adding for Address Multiline field.
            targetedChangedColumns.Add("Address;#Note", "AddressMT");
            //Adding for Content Multiline field.
            targetedChangedColumns.Add("Content;#Note", "ContentMT");
            //Adding for IsActive Choice field.
            targetedChangedColumns.Add("IsActive;#Choice", "IsActiveCH");
            //Adding for TaskType Choice field.
            targetedChangedColumns.Add("TaskType;#Choice", "TaskTypeCH");
            //Adding for ContentType Choice field.
            targetedChangedColumns.Add("ContentType;#Choice", "ContentTypeCH");
            //Adding for Category Choice field.
            targetedChangedColumns.Add("Category;#Choice", "CategoryCH");
            //Adding for Name Choice field.
            targetedChangedColumns.Add("Name;#Choice", "NameCH");
            //Adding for Role Choice field.
            targetedChangedColumns.Add("Location;#Choice", "LocationCH");
            //Adding for Role Choice field.
            targetedChangedColumns.Add("Role;#Choice", "RoleCH");
            //Adding for Active Choice field.
            targetedChangedColumns.Add("Active;#Choice", "IsActiveCH");
            //Adding for Active Yes/No Boolean field.
            targetedChangedColumns.Add("Active;#Boolean", "IsActiveCH");
            //Adding for IsActive Yes/No Boolean field.
            targetedChangedColumns.Add("IsActive;#Boolean", "IsActiveCH");
            //Adding for TimeZone number field.
            targetedChangedColumns.Add("TimeZone;#Number", "TimeZoneNM");
            //Adding for AverageRating number field.
            targetedChangedColumns.Add("AverageRating;#Number", "AverageRatingNM");
            //Adding for DueDate DateTiem field
            targetedChangedColumns.Add("DueDate;#DateTime", "DueDateDT");
            //Adding for EndDate DateTiem field
            targetedChangedColumns.Add("EndDate;#DateTime", "EndDateDT");
            //Adding for UserName Person or Group field
            targetedChangedColumns.Add("UserName;#User", "UserNameToPG");
            //Adding for FullName Calculated field
            targetedChangedColumns.Add("FullName;#Calculated", "FullNameCC");
            return targetedChangedColumns;
        }

        private void GetLookupListName(Field field)
        {
            var lookupField = _sourceClientContext.CastTo<FieldLookup>(field);
            _sourceClientContext.Load(lookupField);
            _sourceClientContext.ExecuteQuery();
            var lookupListId = new Guid(lookupField.LookupList); //returns associated list id
                                                                 //Retrieve associated List
            var lookupList = _sourceClientContext.Web.Lists.GetById(lookupListId);
            _sourceClientContext.Load(lookupList);
            _sourceClientContext.ExecuteQuery();
            Console.WriteLine(lookupList);
        }


    }
}