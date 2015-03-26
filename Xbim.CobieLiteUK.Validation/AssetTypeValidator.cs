﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xbim.CobieLiteUK.Validation.Extensions;
using Xbim.CobieLiteUK.Validation.RequirementDetails;
using Xbim.COBieLiteUK;
using Attribute = Xbim.COBieLiteUK.Attribute;

namespace Xbim.CobieLiteUK.Validation
{


    public class AssetTypeValidator : IValidator
    {
        private readonly AssetType _requirementType;

        public AssetTypeValidator(AssetType requirementType)
        {
            _requirementType = requirementType;
        }

        private List<RequirementDetail> _requirementDetails;

        public List<RequirementDetail> RequirementDetails
        {
            get
            {
                if (_requirementDetails == null)
                    RefreshRequirementDetails();
                return _requirementDetails;
            }
        }

        private void RefreshRequirementDetails()
        {
            _requirementDetails = new List<RequirementDetail>();
            foreach (var attrib in _requirementType.Attributes.Where(x => x.Categories != null && x.Categories.Any(c=>c.Classification=="DPoW" && c.Code=="required" )))
            {
                _requirementDetails.Add(new RequirementDetail(attrib));
            }
        }

        public TerminationMode TerminationMode { get; set; }

        public bool HasFailures { get; private set; }

        public bool HasRequirements 
        {
            get { return RequirementDetails.Any(); }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="candidateType">If null provides a missing match report</param>
        /// <returns></returns>
        internal AssetType Validate(AssetType candidateType)
        {
            var retType = new AssetType
            {
                Categories = new List<Category>(_requirementType.Categories.Clone()) // classification comes from the requirement
            };
            // improve returning assetType
            if (candidateType == null) // the following properties depend on the nullity of candidate
            {
                retType.Name = _requirementType.Name;
                retType.ExternalId = _requirementType.ExternalId;
            }
            else
            {
                retType.Name = candidateType.Name;
                retType.ExternalId = candidateType.ExternalId;
            }

            bool returnWithoutFurtherTests = false;
            if (!RequirementDetails.Any())
            {
                retType.Description = "No requirement for the specific classification.\r\n";
                retType.Categories.Add(FacilityValidator.PassedCat);
                returnWithoutFurtherTests = true;
            }

            // if candidate is null then consider there's no match
            if (candidateType == null)
            {
                retType.Categories.Add(FacilityValidator.FailedCat);
                retType.Description = "No candidates in submission match the required classification.\r\n";
                returnWithoutFurtherTests = true;    
            }

            if (returnWithoutFurtherTests)
                return retType;

            // produce type level description
            var outstandingRequirements = MissingFrom(candidateType);
            var outstandingRequirementsCount = outstandingRequirements.Count();
            retType.Description = string.Format("{0} of {1} requirement addressed at type level.", RequirementDetails.Count - outstandingRequirementsCount, RequirementDetails.Count);
            // todo: can add list of succesful attributes at type level here


            var anyAssetFails = false;
            retType.Assets = new List<Asset>();
            // perform tests at asset level
            foreach (var modelAsset in candidateType.Assets)
            {
                var reportAsset = new Asset
                {
                    Attributes = new List<Attribute>(),
                    Name = modelAsset.Name,
                    AssetIdentifier = modelAsset.AssetIdentifier,
                    Categories = new List<Category>(),
                    ExternalId = modelAsset.ExternalId
                };
                // reportAsset.Description = modelAsset.Description;
                if (modelAsset.Categories != null)
                    reportAsset.Categories.AddRange(modelAsset.Categories);    
                
                // at this stage we are only validating for the existence of attributes.
                //
                var matching = outstandingRequirements.Select(x => x.Name).Intersect(modelAsset.Attributes.Select(at => at.Name));
                var matchingCount = matching.Count();
                // add passes to the report.
                foreach (var matched in matching)
                {
                    var att = new Attribute()
                    {
                        Name = matched,
                        Categories = new List<Category>() { FacilityValidator.PassedCat }
                    };
                    reportAsset.Attributes.Add(att);
                }

                var sb = new StringBuilder();
                sb.AppendFormat("{0} of {1} requirements matched at asset level.\r\n\r\n", matchingCount, outstandingRequirementsCount);

                var pass = (outstandingRequirementsCount == matchingCount);
                if (!pass)
                {
                    anyAssetFails = true;
                    sb.AppendLine("Missing attributes:");
                    foreach (var req in outstandingRequirements)
                    {
                        if (!matching.Contains(req.Name))
                        {
                            sb.AppendFormat("{0}\r\n{1}\r\n\r\n", req.Name, req.Description);
                            
                            var att = new Attribute()
                            {
                                Name = req.Name,
                                Description = req.Description,
                                Categories = new List<Category>() { FacilityValidator.FailedCat }
                            };
                            reportAsset.Attributes.Add(att);
                        }
                    }
                    reportAsset.Categories.Add(FacilityValidator.FailedCat);
                }
                else
                {
                    reportAsset.Categories.Add(FacilityValidator.PassedCat);
                }
                reportAsset.Description = sb.ToString();
                retType.Assets.Add(reportAsset);
            }
            
            retType.Categories.Add(
                anyAssetFails ? FacilityValidator.FailedCat : FacilityValidator.PassedCat
                );
            return retType;
        }


        private IEnumerable<RequirementDetail> MissingFrom(AssetType typeToTest)
        {
            if (typeToTest.Attributes == null)
                return RequirementDetails;

            var req = new HashSet<string>(RequirementDetails.Select(x => x.Name));
            var got = new HashSet<string>(typeToTest.Attributes.Select(x => x.Name));

            req.RemoveWhere(got.Contains);
            return req.Select(left => RequirementDetails.FirstOrDefault(x => x.Name == left));
        }

        private  static readonly Regex NbsCodeCoreMatch= new Regex(@".*/\d+");

        internal IEnumerable<AssetType> GetCandidates(Facility submitted)
        {
            var reqClass = _requirementType.Categories.FirstOrDefault();
            if (reqClass == null)
                return Enumerable.Empty<AssetType>();
            Debug.Write(string.Format("RequirementCode: {0}\r\n", reqClass.Code));

            // todo: we need to clarify the rules for code matching
            // I'm temporarily changing NBS codes to get more matches.

            if (reqClass.Classification == @"NBS Code")
            {
                reqClass = new Category()
                {
                    Classification = @"NBS Code",
                    Code = NbsCodeCoreMatch.Match(reqClass.Code).Value,
                    Description = reqClass.Description
                };
            }
            return submitted.AssetTypes.GetClassificationSubset(reqClass);
        }
    }
}
