// <copyright>
// Copyright 2013 by the Spark Development Network
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>
//
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

using RestSharp;

using Rock;
using Rock.Attribute;
using Rock.Data;
using Rock.Model;
using Rock.Web.Cache;
using Rock.Workflow;

namespace com.bricksandmortar.Workflow.Action
{
    /// <summary>
    /// Sets an attribute's value to the selected person 
    /// </summary>
    [Description( "Fetches a profile photo from Gravatar for a given person." )]
    [Export( typeof( ActionComponent ) )]
    [ExportMetadata( "ComponentName", "Fetch Person Photo from Gravatar" )]

    [WorkflowAttribute( "Person", "Workflow attribute that contains the person to fetch the Gravatar profile photo for.", true, "", "", 0, null,
        new string[] { "Rock.Field.Types.PersonFieldType" } )]
    [IntegerField( "Photo Size", "The photo height in pixels to request from Gravatar.", false, 200, "", 1, "size" )]
    public class GravatarPhotoFetch : ActionComponent
    {
        /// <summary>
        /// Executes the specified workflow.
        /// </summary>
        /// <param name="rockContext">The rock context.</param>
        /// <param name="action">The action.</param>
        /// <param name="entity">The entity.</param>
        /// <param name="errorMessages">The error messages.</param>
        /// <returns></returns>
        public override bool Execute( RockContext rockContext, WorkflowAction action, Object entity, out List<string> errorMessages )
        {
            errorMessages = new List<string>();

            string size = GetAttributeValue( action, "size" ) ?? "200";
            string value = GetAttributeValue( action, "Person" );
            Guid personGuid = value.AsGuid();
            if ( !personGuid.IsEmpty() )
            {
                var attributePerson = AttributeCache.Read( personGuid, rockContext );
                if ( attributePerson != null )
                {
                    string attributePersonValue = action.GetWorklowAttributeValue( personGuid );
                    if ( !string.IsNullOrWhiteSpace( attributePersonValue ) )
                    {
                        if ( attributePerson.FieldType.Class == "Rock.Field.Types.PersonFieldType" )
                        {
                            Guid personAliasGuid = attributePersonValue.AsGuid();
                            if ( !personAliasGuid.IsEmpty() )
                            {
                                var person = new PersonAliasService( rockContext ).Queryable()
                                    .Where( a => a.Guid.Equals( personAliasGuid ) )
                                    .Select( a => a.Person )
                                    .FirstOrDefault();
                                if ( person != null )
                                {
                                    if ( !string.IsNullOrEmpty( person.Email ) )
                                    {
                                        // Build MD5 hash for email
                                        MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider();
                                        byte[] encodedEmail = new UTF8Encoding().GetBytes( person.Email.ToLower().Trim() );
                                        byte[] hashedBytes = md5.ComputeHash( encodedEmail );
                                        StringBuilder sb = new StringBuilder( hashedBytes.Length * 2 );
                                        for ( int i = 0; i < hashedBytes.Length; i++ )
                                        {
                                            sb.Append( hashedBytes[i].ToString( "X2" ) );
                                        }
                                        string hash = sb.ToString().ToLower();
                                        // Query Gravatar's https endpoint asking for a 404 on no match
                                        var restClient = new RestClient( string.Format( "https://secure.gravatar.com/avatar/{0}.jpg?default=404&size={1}", hash, size ) );
                                        var request = new RestRequest( Method.GET );
                                        var response = restClient.Execute( request );
                                        if ( response.StatusCode == HttpStatusCode.OK )
                                        {
                                            var bytes = response.RawBytes;
                                            // Create and save the image
                                            BinaryFileType fileType = new BinaryFileTypeService( rockContext ).Get( Rock.SystemGuid.BinaryFiletype.PERSON_IMAGE.AsGuid() );
                                            if ( fileType != null )
                                            {

                                                var binaryFileService = new BinaryFileService( rockContext );
                                                var binaryFile = new BinaryFile();
                                                binaryFileService.Add( binaryFile );
                                                binaryFile.IsTemporary = false;
                                                binaryFile.BinaryFileType = fileType;
                                                binaryFile.MimeType = "image/jpeg";
                                                binaryFile.FileName = person.NickName + person.LastName + ".jpg";
                                                binaryFile.ContentStream = new MemoryStream( bytes );

                                                person.PhotoId = binaryFile.Id;
                                                rockContext.SaveChanges();
                                            }
                                        }
                                    }
                                    else
                                    {
                                        errorMessages.Add( string.Format( "Email address could not be found for {0} ({1})!", person.FullName.ToString(), personGuid.ToString() ) );
                                    }
                                }
                                else
                                {
                                    errorMessages.Add( string.Format( "Person could not be found for selected value ('{0}')!", personGuid.ToString() ) );
                                }
                            }
                        }
                        else
                        {
                            errorMessages.Add( "The attribute used to provide the person was not of type 'Person'." );
                        }
                    }
                }
            }

            return true;
        }
    }

}